﻿using System;
using System.Threading;
using System.Collections.Generic;
using System.Xml.Linq;
using OpenAL;
using Microsoft.Xna.Framework;
using System.Linq;
using System.IO;

namespace Barotrauma.Sounds
{
    public class SoundManager : IDisposable
    {
        public const int SOURCE_COUNT = 32;

        public bool Disabled
        {
            get;
            private set;
        }
        
        private IntPtr alcDevice;
        private IntPtr alcContext;
        
        public enum SourcePoolIndex
        {
            Default = 0,
            Voice = 1
        }
        private readonly SoundSourcePool[] sourcePools;
        
        private List<Sound> loadedSounds;
        private readonly SoundChannel[][] playingChannels = new SoundChannel[2][];

        private Thread streamingThread;

        private Vector3 listenerPosition;
        public Vector3 ListenerPosition
        {
            get { return listenerPosition; }
            set
            {
                if (Disabled) { return; }
                listenerPosition = value;
                Al.Listener3f(Al.Position,value.X,value.Y,value.Z);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set listener position: " + Al.GetErrorString(alError));
                }
            }
        }

        private float[] listenerOrientation = new float[6];
        public Vector3 ListenerTargetVector
        {
            get { return new Vector3(listenerOrientation[0], listenerOrientation[1], listenerOrientation[2]); }
            set
            {
                if (Disabled) { return; }
                listenerOrientation[0] = value.X; listenerOrientation[1] = value.Y; listenerOrientation[2] = value.Z;
                Al.Listenerfv(Al.Orientation, listenerOrientation);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set listener target vector: " + Al.GetErrorString(alError));
                }
            }
        }
        public Vector3 ListenerUpVector
        {
            get { return new Vector3(listenerOrientation[3], listenerOrientation[4], listenerOrientation[5]); }
            set
            {
                if (Disabled) { return; }
                listenerOrientation[3] = value.X; listenerOrientation[4] = value.Y; listenerOrientation[5] = value.Z;
                Al.Listenerfv(Al.Orientation, listenerOrientation);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set listener up vector: " + Al.GetErrorString(alError));
                }
            }
        }

        private float listenerGain;
        public float ListenerGain
        {
            get { return listenerGain; }
            set
            {
                if (Disabled) { return; }
                if (Math.Abs(ListenerGain - value) < 0.001f) { return; }
                listenerGain = value;
                Al.Listenerf(Al.Gain, listenerGain);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set listener gain: " + Al.GetErrorString(alError));
                }
            }
        }
        
        public float PlaybackAmplitude
        {
            get
            {
                float aggregateAmplitude = 0.0f;
                //NOTE: this is obviously not entirely accurate;
                //It assumes a linear falloff model, and assumes that audio
                //is simply added together to produce the final result.
                //Adjustments may be needed under certain scenarios.
                for (int i=0;i<2;i++)
                {
                    foreach (SoundChannel soundChannel in playingChannels[i].Where(ch => ch != null))
                    {
                        float amplitude = soundChannel.CurrentAmplitude;
                        amplitude *= soundChannel.Gain;
                        float dist = Vector3.Distance(ListenerPosition, soundChannel.Position ?? ListenerPosition);
                        if (dist > soundChannel.Near)
                        {
                            amplitude *= 1.0f - Math.Min(1.0f, (dist - soundChannel.Near) / (soundChannel.Far - soundChannel.Near));
                        }
                        aggregateAmplitude += amplitude;
                    }
                }
                return aggregateAmplitude;
            }
        }
        
        public float CompressionDynamicRangeGain { get; private set; }

        private float voipAttenuatedGain;
        private double lastAttenuationTime;
        public float VoipAttenuatedGain
        {
            get { return voipAttenuatedGain; }
            set
            {
                lastAttenuationTime = Timing.TotalTime;
                voipAttenuatedGain = value;
            }
        }

        public int LoadedSoundCount
        {
            get { return loadedSounds.Count; }
        }
        public int UniqueLoadedSoundCount
        {
            get { return loadedSounds.Select(s => s.Filename).Distinct().Count(); }
        }

        private class CategoryModifier
        {
            public float[] GainMultipliers;
            public bool Muffle;

            public CategoryModifier(int gainMultiplierIndex, float gain, bool muffle)
            {
                Muffle = muffle;
                GainMultipliers = new float[gainMultiplierIndex+1];
                for (int i=0;i<GainMultipliers.Length;i++)
                {
                    if (i==gainMultiplierIndex)
                    {
                        GainMultipliers[i] = gain;
                    }
                    else
                    {
                        GainMultipliers[i] = 1.0f;
                    }
                }
            }

            public void SetGainMultiplier(int index, float gain)
            {
                if (GainMultipliers.Length < index+1)
                {
                    int oldLength = GainMultipliers.Length;
                    Array.Resize(ref GainMultipliers, index + 1);
                    for (int i=oldLength;i<GainMultipliers.Length;i++)
                    {
                        GainMultipliers[i] = 1.0f;
                    }
                }
                GainMultipliers[index] = gain;
            }
        }
        private Dictionary<string, CategoryModifier> categoryModifiers;

        public SoundManager()
        {
            loadedSounds = new List<Sound>();
            streamingThread = null;
            categoryModifiers = null;
            
            alcDevice = Alc.OpenDevice(null);
            if (alcDevice == null)
            {
                DebugConsole.ThrowError("Failed to open an ALC device! Disabling audio playback...");
                Disabled = true;
                return;
            }

            int alcError = Alc.GetError(alcDevice);
            if (alcError != Alc.NoError)
            {
                //The audio device probably wasn't ready, this happens quite often
                //Just wait a while and try again
                Thread.Sleep(100);

                alcDevice = Alc.OpenDevice(null);

                alcError = Alc.GetError(alcDevice);
                if (alcError != Alc.NoError)
                {
                    DebugConsole.ThrowError("Error initializing ALC device: " + alcError.ToString() + ". Disabling audio playback...");
                    Disabled = true;
                    return;
                }
            }

            int[] alcContextAttrs = new int[] { };
            alcContext = Alc.CreateContext(alcDevice, alcContextAttrs);
            if (alcContext == null)
            {
                DebugConsole.ThrowError("Failed to create an ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + "). Disabling audio playback...");
                Disabled = true;
                return;
            }

            if (!Alc.MakeContextCurrent(alcContext))
            {
                DebugConsole.ThrowError("Failed to assign the current ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + "). Disabling audio playback...");
                Disabled = true;
                return;
            }

            alcError = Alc.GetError(alcDevice);
            if (alcError != Alc.NoError)
            {
                DebugConsole.ThrowError("Error after assigning ALC context: " + alcError.ToString() + ". Disabling audio playback...");
                Disabled = true;
                return;
            }

            int alError = Al.NoError;

            sourcePools = new SoundSourcePool[2];
            sourcePools[(int)SourcePoolIndex.Default] = new SoundSourcePool(SOURCE_COUNT);
            playingChannels[(int)SourcePoolIndex.Default] = new SoundChannel[SOURCE_COUNT];

            sourcePools[(int)SourcePoolIndex.Voice] = new SoundSourcePool(16);
            playingChannels[(int)SourcePoolIndex.Voice] = new SoundChannel[16];

            Al.DistanceModel(Al.LinearDistanceClamped);
            
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.ThrowError("Error setting distance model: " + Al.GetErrorString(alError) + ". Disabling audio playback...");
                Disabled = true;
                return;
            }

            ListenerPosition = Vector3.Zero;
            ListenerTargetVector = new Vector3(0.0f, 0.0f, 1.0f);
            ListenerUpVector = new Vector3(0.0f, -1.0f, 0.0f);

            CompressionDynamicRangeGain = 1.0f;
        }

        public Sound LoadSound(string filename, bool stream = false)
        {
            if (Disabled) { return null; }

            if (!File.Exists(filename))
            {
                throw new FileNotFoundException("Sound file \"" + filename + "\" doesn't exist!");
            }

            Sound newSound = new OggSound(this, filename, stream);
            lock (loadedSounds)
            {
                loadedSounds.Add(newSound);
            }
            return newSound;
        }

        public Sound LoadSound(XElement element, bool stream = false)
        {
            if (Disabled) { return null; }

            string filePath = element.GetAttributeString("file", "");
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Sound file \"" + filePath + "\" doesn't exist!");
            }

            var newSound = new OggSound(this, filePath, stream);
            if (newSound != null)
            {
                newSound.BaseGain = element.GetAttributeFloat("volume", 1.0f);
                float range = element.GetAttributeFloat("range", 1000.0f);
                newSound.BaseNear = range * 0.4f;
                newSound.BaseFar = range;
            }

            lock (loadedSounds)
            {
                loadedSounds.Add(newSound);
            }
            return newSound;
        }

        public SoundChannel GetSoundChannelFromIndex(SourcePoolIndex poolIndex, int ind)
        {
            if (Disabled || ind < 0 || ind >= playingChannels[(int)poolIndex].Length) return null;
            return playingChannels[(int)poolIndex][ind];
        }

        public uint GetSourceFromIndex(SourcePoolIndex poolIndex, int srcInd)
        {
            if (Disabled || srcInd < 0 || srcInd >= sourcePools[(int)poolIndex].ALSources.Length) return 0;

            if (!Al.IsSource(sourcePools[(int)poolIndex].ALSources[srcInd]))
            {
                throw new Exception("alSources[" + srcInd.ToString() + "] is invalid!");
            }

            return sourcePools[(int)poolIndex].ALSources[srcInd];
        }

        public int AssignFreeSourceToChannel(SoundChannel newChannel)
        {
            if (Disabled) { return -1; }

            //remove a channel that has stopped
            //or hasn't even been assigned
            int poolIndex = (int)newChannel.Sound.SourcePoolIndex;

            lock (playingChannels[poolIndex])
            {
                for (int i = 0; i < playingChannels[poolIndex].Length; i++)
                {
                    if (playingChannels[poolIndex][i] == null || !playingChannels[poolIndex][i].IsPlaying)
                    {
                        if (playingChannels[poolIndex][i] != null) { playingChannels[poolIndex][i].Dispose(); }
                        playingChannels[poolIndex][i] = newChannel;
                        return i;
                    }
                }
            }

            //we couldn't get a free source to assign to this channel!
            return -1;
        }

#if DEBUG
        public void DebugSource(int ind)
        {
            for (int i = 0; i < sourcePools[0].ALSources.Length; i++)
            {
                Al.Sourcef(sourcePools[0].ALSources[i], Al.MaxGain, i == ind ? 1.0f : 0.0f);
                Al.Sourcef(sourcePools[0].ALSources[i], Al.MinGain, 0.0f);
            }
        }
#endif

        public bool IsPlaying(Sound sound)
        {
            if (Disabled) { return false; }
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i] != null && 
                        playingChannels[(int)sound.SourcePoolIndex][i].Sound == sound)
                    {
                        if (playingChannels[(int)sound.SourcePoolIndex][i].IsPlaying) return true;
                    }
                }
            }
            return false;
        }

        public int CountPlayingInstances(Sound sound)
        {
            if (Disabled) { return 0; }
            int count = 0;
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i] != null && 
                        playingChannels[(int)sound.SourcePoolIndex][i].Sound.Filename == sound.Filename)
                    {
                        if (playingChannels[(int)sound.SourcePoolIndex][i].IsPlaying) { count++; };
                    }
                }
            }
            return count;
        }

        public SoundChannel GetChannelFromSound(Sound sound)
        {
            if (Disabled) { return null; }
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i] != null && 
                        playingChannels[(int)sound.SourcePoolIndex][i].Sound == sound)
                    {
                        if (playingChannels[(int)sound.SourcePoolIndex][i].IsPlaying) return playingChannels[(int)sound.SourcePoolIndex][i];
                    }
                }
            }
            return null;
        }

        public void KillChannels(Sound sound)
        {
            if (Disabled) { return; }
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i]!=null && playingChannels[(int)sound.SourcePoolIndex][i].Sound == sound)
                    {
                        playingChannels[(int)sound.SourcePoolIndex][i]?.Dispose();
                        playingChannels[(int)sound.SourcePoolIndex][i] = null;
                    }
                }
            }
        }

        public void RemoveSound(Sound sound)
        {
            lock (loadedSounds)
            {
                for (int i = 0; i < loadedSounds.Count; i++)
                {
                    if (loadedSounds[i] == sound)
                    {
                        loadedSounds.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        public void SetCategoryGainMultiplier(string category, float gain, int index=0)
        {
            if (Disabled) { return; }
            category = category.ToLower();
            if (categoryModifiers == null) categoryModifiers = new Dictionary<string, CategoryModifier>();
            if (!categoryModifiers.ContainsKey(category))
            {
                categoryModifiers.Add(category, new CategoryModifier(index, gain, false));
            }
            else
            {
                categoryModifiers[category].SetGainMultiplier(index, gain);
            }

            for (int i = 0; i < playingChannels.Length; i++)
            {
                lock (playingChannels[i])
                {
                    for (int j = 0; j < playingChannels[i].Length; j++)
                    {
                        if (playingChannels[i][j] != null && playingChannels[i][j].IsPlaying)
                        {
                            playingChannels[i][j].Gain = playingChannels[i][j].Gain; //force all channels to recalculate their gain
                        }
                    }
                }
            }
        }

        public float GetCategoryGainMultiplier(string category, int index=-1)
        {
            if (Disabled) { return 0.0f; }
            category = category.ToLower();
            if (categoryModifiers == null || !categoryModifiers.ContainsKey(category)) return 1.0f;
            if (index < 0)
            {
                float accumulatedMultipliers = 1.0f;
                for (int i=0;i<categoryModifiers[category].GainMultipliers.Length;i++)
                {
                    accumulatedMultipliers *= categoryModifiers[category].GainMultipliers[i];
                }
                return accumulatedMultipliers;
            }
            else
            {
                return categoryModifiers[category].GainMultipliers[index];
            }
        }

        public void SetCategoryMuffle(string category,bool muffle)
        {
            if (Disabled) { return; }

            category = category.ToLower();

            if (categoryModifiers == null) categoryModifiers = new Dictionary<string, CategoryModifier>();
            if (!categoryModifiers.ContainsKey(category))
            {
                categoryModifiers.Add(category, new CategoryModifier(0, 1.0f, muffle));
            }
            else
            {
                categoryModifiers[category].Muffle = muffle;
            }

            for (int i = 0; i < playingChannels.Length; i++)
            {
                lock (playingChannels[i])
                {
                    for (int j = 0; j < playingChannels[i].Length; j++)
                    {
                        if (playingChannels[i][j] != null && playingChannels[i][j].IsPlaying)
                        {
                            if (playingChannels[i][j].Category.ToLower() == category) playingChannels[i][j].Muffled = muffle;
                        }
                    }
                }
            }
        }

        public bool GetCategoryMuffle(string category)
        {
            if (Disabled) { return false; }

            category = category.ToLower();
            if (categoryModifiers == null || !categoryModifiers.ContainsKey(category)) return false;
            return categoryModifiers[category].Muffle;
        }

        public void Update()
        {
            if (GameMain.Client != null && GameMain.Config.VoipAttenuationEnabled)
            {
                if (Timing.TotalTime > lastAttenuationTime+0.2)
                {
                    voipAttenuatedGain = voipAttenuatedGain * 0.9f + 0.1f;
                }
            }
            else
            {
                voipAttenuatedGain = 1.0f;
            }
            SetCategoryGainMultiplier("default", VoipAttenuatedGain, 1);
            SetCategoryGainMultiplier("ui", VoipAttenuatedGain, 1);
            SetCategoryGainMultiplier("waterambience", VoipAttenuatedGain, 1);
            SetCategoryGainMultiplier("music", VoipAttenuatedGain, 1);

            if (GameMain.Config.DynamicRangeCompressionEnabled)
            {
                float targetGain = (Math.Min(1.0f, 1.0f / PlaybackAmplitude) - 1.0f) * 0.5f + 1.0f;
                if (targetGain < CompressionDynamicRangeGain)
                {
                    //if the target gain is lower than the current gain, lower the current gain immediately to prevent clipping
                    CompressionDynamicRangeGain = targetGain;
                }
                else
                {
                    //otherwise, let it rise back smoothly
                    CompressionDynamicRangeGain = (targetGain) * 0.05f + CompressionDynamicRangeGain * 0.95f;
                }
            }
            else
            {
                CompressionDynamicRangeGain = 1.0f;
            }
        }

        public void InitStreamThread()
        {
            if (Disabled) { return; }
            if (streamingThread == null || streamingThread.ThreadState.HasFlag(ThreadState.Stopped))
            {
                streamingThread = new Thread(UpdateStreaming)
                {
                    Name = "SoundManager Streaming Thread",
                    IsBackground = true //this should kill the thread if the game crashes
                };
                streamingThread.Start();
            }
        }

        void UpdateStreaming()
        {
            bool areStreamsPlaying = true;
            while (areStreamsPlaying)
            {
                areStreamsPlaying = false;

                for (int i = 0; i < playingChannels.Length; i++)
                {
                    lock (playingChannels[i])
                    {
                        for (int j = 0; j < playingChannels[i].Length; j++)
                        {
                            if (playingChannels[i][j] == null) { continue; }
                            if (playingChannels[i][j].IsStream)
                            {
                                if (playingChannels[i][j].IsPlaying)
                                {
                                    areStreamsPlaying = true;
                                    playingChannels[i][j].UpdateStream();
                                }
                                else
                                {
                                    playingChannels[i][j].Dispose();
                                }
                            }
                            else if (playingChannels[i][j].FadingOutAndDisposing)
                            {
                                playingChannels[i][j].Gain -= 0.1f;
                                if (playingChannels[i][j].Gain <= 0.0f)
                                {
                                    playingChannels[i][j].Dispose();
                                }
                            }
                        }
                    }
                }
                Thread.Sleep(10); //TODO: use a separate thread for network audio?
            }
        }

        public void Dispose()
        {
            if (Disabled) { return; }

            for (int i = 0; i < playingChannels.Length; i++)
            {
                lock (playingChannels[i])
                {
                    for (int j = 0; j < playingChannels[i].Length; j++)
                    {
                        if (playingChannels[i][j] != null) playingChannels[i][j].Dispose();
                    }
                }
            }

            streamingThread?.Join();
            for (int i = loadedSounds.Count - 1; i >= 0; i--)
            {
                loadedSounds[i].Dispose();
            }
            sourcePools[(int)SourcePoolIndex.Default]?.Dispose();
            sourcePools[(int)SourcePoolIndex.Voice]?.Dispose();

            if (!Alc.MakeContextCurrent(IntPtr.Zero))
            {
                throw new Exception("Failed to detach the current ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + ")");
            }

            Alc.DestroyContext(alcContext);
            
            if (!Alc.CloseDevice(alcDevice))
            {
                throw new Exception("Failed to close ALC device!");
            }
        }
    }
}
