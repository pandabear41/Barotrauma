﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public enum WearableType
    {
        Item,
        Hair,
        Beard,
        Moustache,
        FaceAttachment,
        JobIndicator,
        Husk,
        Herpes
    }

    class WearableSprite
    {
        public string SpritePath { get; private set; }
        public XElement SourceElement { get; private set; }

        public WearableType Type { get; private set; }
        private Sprite _sprite;
        public Sprite Sprite
        {
            get { return _sprite; }
            set
            {
                if (value == _sprite) { return; }
                if (_sprite != null)
                {
                    _sprite.Remove();
                }
                _sprite = value;
            }
        }
        public LimbType Limb { get; private set; }
        public bool HideLimb { get; private set; }
        public bool HideOtherWearables { get; private set; }
        public bool InheritLimbDepth { get; private set; }
        public bool InheritTextureScale { get; private set; }
        public bool InheritOrigin { get; private set; }
        public bool InheritSourceRect { get; private set; }
        public LimbType DepthLimb { get; private set; }
        private Wearable _wearableComponent;
        public Wearable WearableComponent
        {
            get { return _wearableComponent; }
            set
            {
                if (value == _wearableComponent) { return; }
                if (_wearableComponent != null)
                {
                    _wearableComponent.Remove();
                }
                _wearableComponent = value;
            }
        }
        public string Sound { get; private set; }
        public Point? SheetIndex { get; private set; }

        public LightComponent LightComponent { get; set; }

        public int Variant { get; set; }

        private Gender _gender;
        /// <summary>
        /// None = Any/Not Defined -> no effect.
        /// Changing the gender forces re-initialization, because the textures can be different for male and female characters.
        /// </summary>
        public Gender Gender
        {
            get { return _gender; }
            set
            {
                if (value == _gender) { return; }
                _gender = value;
                IsInitialized = false;
                SpritePath = ParseSpritePath(SourceElement.GetAttributeString("texture", string.Empty));
                Init(_gender);
            }
        }

        public WearableSprite(XElement subElement, WearableType type)
        {
            Type = type;
            SourceElement = subElement;
            SpritePath = subElement.GetAttributeString("texture", string.Empty);
            Init();
            switch (type)
            {
                case WearableType.Hair:
                case WearableType.Beard:
                case WearableType.Moustache:
                case WearableType.FaceAttachment:
                case WearableType.JobIndicator:
                case WearableType.Husk:
                case WearableType.Herpes:
                    Limb = LimbType.Head;
                    HideLimb = false;
                    HideOtherWearables = false;
                    InheritLimbDepth = true;
                    InheritTextureScale = true;
                    InheritOrigin = true;
                    InheritSourceRect = true;
                    break;
            }
        }

        /// <summary>
        /// Note: this constructor cannot initialize automatically, because the gender is unknown at this point. We only know it when the item is equipped.
        /// </summary>
        public WearableSprite(XElement subElement, Wearable wearable, int variant = 0)
        {
            Type = WearableType.Item;
            WearableComponent = wearable;
            Variant = Math.Max(variant, 0);
            SpritePath = ParseSpritePath(subElement.GetAttributeString("texture", string.Empty));
            SourceElement = subElement;
        }

        private string ParseSpritePath(string texturePath) => texturePath.Contains("/") ? texturePath : $"{Path.GetDirectoryName(WearableComponent.Item.Prefab.ConfigFile)}/{texturePath}";

        public void RefreshPath()
        {
            if (Variant > 0)
            {
                // Restore the tag so that we can parse it again.
                ReplaceNumbersWith("[VARIANT]");
            }
            ParsePath(true);
        }

        private void ReplaceNumbersWith(string replacement)
        {
            var fileName = Path.GetFileName(SpritePath);
            var path = Path.GetDirectoryName(SpritePath);
            fileName = fileName.Replace(replacement, c => char.IsNumber(c));
            SpritePath = Path.Combine(path, fileName);
        }

        private void ParsePath(bool parseSpritePath)
        {
            if (_gender != Gender.None)
            {
                SpritePath = SpritePath.Replace("[GENDER]", (_gender == Gender.Female) ? "female" : "male");
            }
            SpritePath = SpritePath.Replace("[VARIANT]", Variant.ToString());
            if (!File.Exists(SpritePath))
            {
                // If the variant does not exist, parse the path so that it uses first variant.
                Variant = 1;
                ReplaceNumbersWith(Variant.ToString());
            }
            if (parseSpritePath)
            {
                Sprite.ParseTexturePath(file: SpritePath);
            }
        }

        public bool IsInitialized { get; private set; }
        public void Init(Gender gender = Gender.None)
        {
            if (IsInitialized) { return; }
            _gender = SpritePath.Contains("[GENDER]") ? gender : Gender.None;
            ParsePath(false);
            if (Sprite != null)
            {
                Sprite.Remove();
            }
            Sprite = new Sprite(SourceElement, file: SpritePath);
            Limb = (LimbType)Enum.Parse(typeof(LimbType), SourceElement.GetAttributeString("limb", "Head"), true);
            HideLimb = SourceElement.GetAttributeBool("hidelimb", false);
            HideOtherWearables = SourceElement.GetAttributeBool("hideotherwearables", false);
            InheritLimbDepth = SourceElement.GetAttributeBool("inheritlimbdepth", true);
            InheritTextureScale = SourceElement.GetAttributeBool("inherittexturescale", false);
            InheritOrigin = SourceElement.GetAttributeBool("inheritorigin", false);
            InheritSourceRect = SourceElement.GetAttributeBool("inheritsourcerect", false);
            DepthLimb = (LimbType)Enum.Parse(typeof(LimbType), SourceElement.GetAttributeString("depthlimb", "None"), true);
            Sound = SourceElement.GetAttributeString("sound", "");
            var index = SourceElement.GetAttributePoint("sheetindex", new Point(-1, -1));
            if (index.X > -1 && index.Y > -1)
            {
                SheetIndex = index;
            }
            IsInitialized = true;
        }
    }
}

namespace Barotrauma.Items.Components
{
    class Wearable : Pickable
    {
        private WearableSprite[] wearableSprites;
        private LimbType[] limbType;
        private Limb[] limb;

        private List<DamageModifier> damageModifiers;

        public List<DamageModifier> DamageModifiers
        {
            get { return damageModifiers; }
        }

        private bool autoEquipWhenFull;
        public bool AutoEquipWhenFull
        {
            get { return autoEquipWhenFull; }
        }               
        
        public Wearable(Item item, XElement element) : base(item, element)
        {
            this.item = item;

            damageModifiers = new List<DamageModifier>();
            
            int spriteCount = element.Elements().Count(x => x.Name.ToString() == "sprite");
            int variants = element.GetAttributeInt("variants", 0);
            int variant = variants > 0 ? Rand.Range(1, variants + 1, Rand.RandSync.Server) : 1;
            wearableSprites = new WearableSprite[spriteCount];
            limbType    = new LimbType[spriteCount];
            limb        = new Limb[spriteCount];
            autoEquipWhenFull = element.GetAttributeBool("autoequipwhenfull", true);
            int i = 0;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "sprite":
                        if (subElement.Attribute("texture") == null)
                        {
                            DebugConsole.ThrowError("Item \"" + item.Name + "\" doesn't have a texture specified!");
                            return;
                        }

                        limbType[i] = (LimbType)Enum.Parse(typeof(LimbType),
                            subElement.GetAttributeString("limb", "Head"), true);

                        wearableSprites[i] = new WearableSprite(subElement, this, variant);

                        foreach (XElement lightElement in subElement.Elements())
                        {
                            if (lightElement.Name.ToString().ToLowerInvariant() != "lightcomponent") continue;
                            wearableSprites[i].LightComponent = new LightComponent(item, lightElement)
                            {
                                Parent = this
                            };
                            item.AddComponent(wearableSprites[i].LightComponent);
                        }

                        i++;
                        break;
                    case "damagemodifier":
                        damageModifiers.Add(new DamageModifier(subElement, item.Name + ", Wearable"));
                        break;
                }
            }
        }

        public override void Equip(Character character)
        {
            picker = character;
            for (int i = 0; i < wearableSprites.Length; i++ )
            {
                var wearableSprite = wearableSprites[i];
                if (!wearableSprite.IsInitialized) { wearableSprite.Init(picker.Info?.Gender ?? Gender.None); }
                if (picker.Info?.Gender != Gender.None && (wearableSprite.Gender != Gender.None))
                {
                    // If the item is gender specific (it has a different textures for male and female), we have to change the gender here so that the texture is updated.
                    wearableSprite.Gender = picker.Info.Gender;
                }

                Limb equipLimb  = character.AnimController.GetLimb(limbType[i]);
                if (equipLimb == null) { continue; }
                
                if (item.body != null)
                {
                    item.body.Enabled = false;
                }
                IsActive = true;
                if (wearableSprite.LightComponent != null)
                {
                    wearableSprite.LightComponent.ParentBody = equipLimb.body;
                }

                limb[i] = equipLimb;
                if (!equipLimb.WearingItems.Contains(wearableSprite))
                {
                    equipLimb.WearingItems.Add(wearableSprite);
                    equipLimb.WearingItems.Sort((i1, i2) => { return i2.Sprite.Depth.CompareTo(i1.Sprite.Depth); });
                    equipLimb.WearingItems.Sort((i1, i2) => 
                    {
                        if (i1?.WearableComponent == null && i2?.WearableComponent == null)
                        {
                            return 0;
                        }
                        else if (i1?.WearableComponent == null)
                        {
                            return -1;
                        }
                        else if (i2?.WearableComponent == null)
                        {
                            return 1;
                        }
                        return i1.WearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes).CompareTo(i2.WearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes));
                    });
                }
            }
        }

        public override void Drop(Character dropper)
        {
            Unequip(picker);

            base.Drop(dropper);

            picker = null;
            IsActive = false;
        }

        public override void Unequip(Character character)
        {
            if (picker == null) return;
            for (int i = 0; i < wearableSprites.Length; i++)
            {
                Limb equipLimb = character.AnimController.GetLimb(limbType[i]);
                if (equipLimb == null) continue;

                if (wearableSprites[i].LightComponent != null)
                {
                    wearableSprites[i].LightComponent.ParentBody = null;
                }

                equipLimb.WearingItems.RemoveAll(w => w != null && w == wearableSprites[i]);

                limb[i] = null;
            }

            IsActive = false;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (picker.Removed)
            {
                IsActive = false;
                return;
            }

            item.SetTransform(picker.SimPosition, 0.0f);
            item.SetContainedItemPositions();
            
            item.ApplyStatusEffects(ActionType.OnWearing, deltaTime, picker);

#if CLIENT
            PlaySound(ActionType.OnWearing, picker.WorldPosition, picker);
#endif
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            foreach (WearableSprite wearableSprite in wearableSprites)
            {
                if (wearableSprite != null && wearableSprite.Sprite != null) wearableSprite.Sprite.Remove();
            }
        }

    }
}
