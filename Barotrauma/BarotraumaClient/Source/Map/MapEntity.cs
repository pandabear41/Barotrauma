﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    abstract partial class MapEntity : Entity
    {
        protected static Vector2 selectionPos = Vector2.Zero;
        protected static Vector2 selectionSize = Vector2.Zero;

        protected static Vector2 startMovingPos = Vector2.Zero;

        private static bool resizing;
        private int resizeDirX, resizeDirY;

        //which entities have been selected for editing
        private static List<MapEntity> selectedList = new List<MapEntity>();
        public static List<MapEntity> SelectedList
        {
            get
            {
                return selectedList;
            }
        }
        private static List<MapEntity> copiedList = new List<MapEntity>();

        private static List<MapEntity> highlightedList = new List<MapEntity>();

        // Test feature. Not yet saved.
        public static Dictionary<MapEntity, List<MapEntity>> SelectionGroups { get; private set; } = new Dictionary<MapEntity, List<MapEntity>>();

        private static float highlightTimer;

        private static GUIListBox highlightedListBox;
        public static GUIListBox HighlightedListBox
        {
            get { return highlightedListBox; }
        }


        protected static GUIComponent editingHUD;
        public static GUIComponent EditingHUD
        {
            get
            {
                return editingHUD;
            }
        }

        //protected bool isSelected;

        private static bool disableSelect;
        public static bool DisableSelect
        {
            get { return disableSelect; }
            set
            {
                disableSelect = value;
                if (disableSelect)
                {
                    startMovingPos = Vector2.Zero;
                    selectionSize = Vector2.Zero;
                    selectionPos = Vector2.Zero;
                }
            }
        }

        public virtual bool SelectableInEditor
        {
            get { return true; }
        }

        public static bool SelectedAny
        {
            get { return selectedList.Count > 0; }
        }

        public bool IsSelected
        {
            get { return selectedList.Contains(this); }
        }

        public virtual bool IsVisible(Rectangle worldView)
        {
            return true;
        }

        public virtual void Draw(SpriteBatch spriteBatch, bool editing, bool back = true) { }

        public virtual void DrawDamage(SpriteBatch spriteBatch, Effect damageEffect) { }

        /// <summary>
        /// Update the selection logic in submarine editor
        /// </summary>
        public static void UpdateSelecting(Camera cam)
        {
            if (resizing)
            {
                if (selectedList.Count == 0) resizing = false;
                return;
            }

            foreach (MapEntity e in mapEntityList)
            {
                e.isHighlighted = false;
            }

            if (DisableSelect)
            {
                DisableSelect = false;
                return;
            }

            if (GUI.MouseOn != null || !PlayerInput.MouseInsideWindow)
            {
                if (highlightedListBox == null ||
                    (GUI.MouseOn != highlightedListBox && !highlightedListBox.IsParentOf(GUI.MouseOn)))
                {
                    UpdateHighlightedListBox(null);
                    return;
                }
            }

            if (MapEntityPrefab.Selected != null)
            {
                selectionPos = Vector2.Zero;
                selectedList.Clear();
                return;
            }
            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (PlayerInput.KeyDown(Keys.Delete))
                {
                    selectedList.ForEach(e => e.Remove());
                    selectedList.Clear();
                }

                if (PlayerInput.KeyDown(Keys.LeftControl) || PlayerInput.KeyDown(Keys.RightControl))
                {
                    if (PlayerInput.KeyHit(Keys.C))
                    {
                        CopyEntities(selectedList);
                    }
                    else if (PlayerInput.KeyHit(Keys.X))
                    {
                        CopyEntities(selectedList);

                        selectedList.ForEach(e => e.Remove());
                        selectedList.Clear();
                    }
                    else if (copiedList.Count > 0 && PlayerInput.KeyHit(Keys.V))
                    {
                        List<MapEntity> prevEntities = new List<MapEntity>(mapEntityList);
                        Clone(copiedList);

                        var clones = mapEntityList.Except(prevEntities).ToList();

                        Vector2 center = Vector2.Zero;
                        clones.ForEach(c => center += c.WorldPosition);
                        center = Submarine.VectorToWorldGrid(center / clones.Count);

                        Vector2 moveAmount = Submarine.VectorToWorldGrid(cam.WorldViewCenter - center);

                        selectedList = new List<MapEntity>(clones);
                        foreach (MapEntity clone in selectedList)
                        {
                            clone.Move(moveAmount);
                            clone.Submarine = Submarine.MainSub;
                        }
                    }
                    else if (PlayerInput.KeyHit(Keys.G))
                    {
                        if (selectedList.Any())
                        {
                            if (SelectionGroups.ContainsKey(selectedList.Last()))
                            {
                                // Ungroup all selected
                                selectedList.ForEach(e => SelectionGroups.Remove(e));
                            }
                            else
                            {
                                foreach (var entity in selectedList)
                                {
                                    // Remove the old group, if any
                                    SelectionGroups.Remove(entity);
                                    // Create a group that can be accessed with any member
                                    SelectionGroups.Add(entity, selectedList);
                                }
                            }
                        }
                    }
                    else if (PlayerInput.KeyHit(Keys.Z))
                    {
                        SetPreviousRects(e => e.rectMemento.Undo());
                    }
                    else if (PlayerInput.KeyHit(Keys.R))
                    {
                        SetPreviousRects(e => e.rectMemento.Redo());
                    }

                    void SetPreviousRects(Func<MapEntity, Rectangle> memoryMethod)
                    {
                        foreach (var e in SelectedList)
                        {
                            if (e.rectMemento != null)
                            {
                                Point diff = memoryMethod(e).Location - e.Rect.Location;
                                // We have to call the move method, because there's a lot more than just storing the rect (in some cases)
                                // We also have to reassign the rect, because the move method does not set the width and height. They might have changed too.
                                // The Rect property is virtual and it's overridden for structs. Setting the rect via the property should automatically recreate the sections for resizable structures.
                                e.Move(diff.ToVector2());
                                e.Rect = e.rectMemento.Current;
                            }
                        }
                    }
                }
            }            

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
            MapEntity highLightedEntity = null;
            if (startMovingPos == Vector2.Zero)
            {
                List<MapEntity> highlightedEntities = new List<MapEntity>();
                if (highlightedListBox != null && highlightedListBox.IsParentOf(GUI.MouseOn))
                {
                    highLightedEntity = GUI.MouseOn.UserData as MapEntity;
                }
                else
                {
                    foreach (MapEntity e in mapEntityList)
                    {
                        if (!e.SelectableInEditor) continue;

                        if (e.IsMouseOn(position))
                        {
                            int i = 0;
                            while (i < highlightedEntities.Count &&
                                e.Sprite != null &&
                                (highlightedEntities[i].Sprite == null || highlightedEntities[i].SpriteDepth < e.SpriteDepth))
                            {
                                i++;
                            }

                            highlightedEntities.Insert(i, e);

                            if (i == 0) highLightedEntity = e;
                        }
                    }

                    if (PlayerInput.MouseSpeed.LengthSquared() > 10)
                    {
                        highlightTimer = 0.0f;
                    }
                    else
                    {
                        bool mouseNearHighlightBox = false;

                        if (highlightedListBox != null)
                        {
                            Rectangle expandedRect = highlightedListBox.Rect;
                            expandedRect.Inflate(20, 20);
                            mouseNearHighlightBox = expandedRect.Contains(PlayerInput.MousePosition);
                            if (!mouseNearHighlightBox) highlightedListBox = null;
                        }

                        highlightTimer += (float)Timing.Step;
                        if (highlightTimer > 1.0f)
                        {
                            if (!mouseNearHighlightBox)
                            {
                                UpdateHighlightedListBox(highlightedEntities);
                                highlightTimer = 0.0f;
                            }
                        }
                    }
                }

                if (highLightedEntity != null) highLightedEntity.isHighlighted = true;
            }

            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                Vector2 nudgeAmount = Vector2.Zero;
                if (PlayerInput.KeyHit(Keys.Up))    nudgeAmount.Y = 1f;
                if (PlayerInput.KeyHit(Keys.Down))  nudgeAmount.Y = -1f;
                if (PlayerInput.KeyHit(Keys.Left))  nudgeAmount.X = -1f;
                if (PlayerInput.KeyHit(Keys.Right)) nudgeAmount.X = 1f;            
                if (nudgeAmount != Vector2.Zero)
                {
                    foreach (MapEntity entityToNudge in selectedList)
                    {
                        entityToNudge.Move(nudgeAmount);
                    }
                }
            }

            //started moving selected entities
            if (startMovingPos != Vector2.Zero)
            {
                if (PlayerInput.LeftButtonReleased())
                {
                    //mouse released -> move the entities to the new position of the mouse

                    Vector2 moveAmount = position - startMovingPos;
                    moveAmount.X = (float)(moveAmount.X > 0.0f ? Math.Floor(moveAmount.X / Submarine.GridSize.X) : Math.Ceiling(moveAmount.X / Submarine.GridSize.X)) * Submarine.GridSize.X;
                    moveAmount.Y = (float)(moveAmount.Y > 0.0f ? Math.Floor(moveAmount.Y / Submarine.GridSize.Y) : Math.Ceiling(moveAmount.Y / Submarine.GridSize.Y)) * Submarine.GridSize.Y;
                    if (Math.Abs(moveAmount.X) >= Submarine.GridSize.X || Math.Abs(moveAmount.Y) >= Submarine.GridSize.Y)
                    {
                        moveAmount = Submarine.VectorToWorldGrid(moveAmount);
                        //clone
                        if (PlayerInput.KeyDown(Keys.LeftControl) || PlayerInput.KeyDown(Keys.RightControl))
                        {
                            var clones = Clone(selectedList);
                            selectedList = clones;
                            selectedList.ForEach(c => c.Move(moveAmount));
                        }
                        else // move
                        {
                            foreach (MapEntity e in selectedList)
                            {
                                if (e.rectMemento == null)
                                {
                                    e.rectMemento = new Memento<Rectangle>();
                                    e.rectMemento.Store(e.Rect);
                                }
                                e.Move(moveAmount);
                                e.rectMemento.Store(e.Rect);
                            }
                        }
                    }
                    startMovingPos = Vector2.Zero;
                }

            }
            //started dragging a "selection rectangle"
            else if (selectionPos != Vector2.Zero)
            {
                selectionSize.X = position.X - selectionPos.X;
                selectionSize.Y = selectionPos.Y - position.Y;

                List<MapEntity> newSelection = new List<MapEntity>();// FindSelectedEntities(selectionPos, selectionSize);
                if (Math.Abs(selectionSize.X) > Submarine.GridSize.X || Math.Abs(selectionSize.Y) > Submarine.GridSize.Y)
                {
                    newSelection = FindSelectedEntities(selectionPos, selectionSize);
                }
                else
                {
                    if (highLightedEntity != null)
                    {
                        if (SelectionGroups.TryGetValue(highLightedEntity, out List<MapEntity> group))
                        {
                            newSelection.AddRange(group);
                        }
                        else
                        {
                            newSelection.Add(highLightedEntity);
                        }
                    }
                }

                if (PlayerInput.LeftButtonReleased())
                {
                    if (PlayerInput.KeyDown(Keys.LeftControl) ||
                        PlayerInput.KeyDown(Keys.RightControl))
                    {
                        foreach (MapEntity e in newSelection)
                        {
                            if (selectedList.Contains(e))
                            {
                                RemoveSelection(e);
                            }
                            else
                            {
                                AddSelection(e);
                            }
                        }
                    }
                    else
                    {
                        selectedList = new List<MapEntity>(newSelection);
                        //selectedList.Clear();
                        //newSelection.ForEach(e => AddSelection(e));
                        foreach (var entity in newSelection)
                        {
                            HandleDoorGapLinks(entity,
                                onGapFound: (door, gap) =>
                                {
                                    door.RefreshLinkedGap();
                                    if (!selectedList.Contains(gap))
                                    {
                                        selectedList.Add(gap);
                                    }
                                },
                                onDoorFound: (door, gap) =>
                                {
                                    if (!selectedList.Contains(door.Item))
                                    {
                                        selectedList.Add(door.Item);
                                    }
                                });
                        }
                    }

                    //select wire if both items it's connected to are selected
                    var selectedItems = selectedList.Where(e => e is Item).Cast<Item>().ToList();
                    foreach (Item item in selectedItems)
                    {
                        if (item.Connections == null) continue;
                        foreach (Connection c in item.Connections)
                        {
                            foreach (Wire w in c.Wires)
                            {
                                if (w == null || selectedList.Contains(w.Item)) continue;

                                if (w.OtherConnection(c) != null && selectedList.Contains(w.OtherConnection(c).Item))
                                {
                                    selectedList.Add(w.Item);
                                }
                            }
                        }
                    }

                    selectionPos = Vector2.Zero;
                    selectionSize = Vector2.Zero;
                }
            }
            //default, not doing anything specific yet
            else
            {
                if (PlayerInput.LeftButtonHeld() &&
                    PlayerInput.KeyUp(Keys.Space) &&
                    (highlightedListBox == null || (GUI.MouseOn != highlightedListBox && !highlightedListBox.IsParentOf(GUI.MouseOn))))
                {
                    //if clicking a selected entity, start moving it
                    foreach (MapEntity e in selectedList)
                    {
                        if (e.IsMouseOn(position)) startMovingPos = position;
                    }
                    selectionPos = position;

                    //stop camera movement to prevent accidental dragging or rect selection
                    Screen.Selected.Cam.StopMovement();
                }
            }
        }

        private static void UpdateHighlightedListBox(List<MapEntity> highlightedEntities)
        {
            if (highlightedEntities == null || highlightedEntities.Count < 2)
            {
                highlightedListBox = null;
                return;
            }
            if (highlightedListBox != null)
            {
                if (GUI.MouseOn == highlightedListBox || highlightedListBox.IsParentOf(GUI.MouseOn)) return;
                if (highlightedEntities.SequenceEqual(highlightedList)) return;
            }

            highlightedList = highlightedEntities;

            highlightedListBox = new GUIListBox(new RectTransform(new Point(180, highlightedEntities.Count * 18 + 5), GUI.Canvas)
            {
                ScreenSpaceOffset =  PlayerInput.MousePosition.ToPoint() + new Point(15)
            }, style: "GUIToolTip");

            foreach (MapEntity entity in highlightedEntities)
            {
                var textBlock = new GUITextBlock(new RectTransform(new Point(highlightedListBox.Content.Rect.Width, 15), highlightedListBox.Content.RectTransform),
                    ToolBox.LimitString(entity.Name, GUI.SmallFont, 140), font: GUI.SmallFont)
                {
                    UserData = entity
                };
            }

            highlightedListBox.OnSelected = (GUIComponent component, object obj) =>
            {
                MapEntity entity = obj as MapEntity;

                if (PlayerInput.KeyDown(Keys.LeftControl) ||
                    PlayerInput.KeyDown(Keys.RightControl))
                {
                    if (selectedList.Contains(entity))
                    {
                        RemoveSelection(entity);
                    }
                    else
                    {
                        AddSelection(entity);
                    }
                }
                else
                {
                    SelectEntity(entity);
                }

                return true;
            };
        }

        public static void AddSelection(MapEntity entity)
        {
            if (selectedList.Contains(entity)) { return; }
            selectedList.Add(entity);
            HandleDoorGapLinks(entity, 
                onGapFound: (door, gap) =>
                {
                    door.RefreshLinkedGap();
                    if (!selectedList.Contains(gap))
                    {
                        selectedList.Add(gap);
                    }
                }, 
                onDoorFound: (door, gap) => 
                {
                    if (!selectedList.Contains(door.Item))
                    {
                        selectedList.Add(door.Item);
                    }
                });
        }

        private static void HandleDoorGapLinks(MapEntity entity, Action<Door, Gap> onGapFound, Action<Door, Gap> onDoorFound)
        {
            if (entity is Item i)
            {
                var door = i.GetComponent<Door>();
                if (door != null)
                {
                    var gap = door.LinkedGap;
                    if (gap != null)
                    {
                        onGapFound(door, gap);
                    }
                }
            }
            else if (entity is Gap gap)
            {
                var door = gap.ConnectedDoor;
                if (door != null)
                {
                    onDoorFound(door, gap);
                }
            }
        }

        public static void RemoveSelection(MapEntity entity)
        {
            selectedList.Remove(entity);
            HandleDoorGapLinks(entity,
                onGapFound: (door, gap) => selectedList.Remove(gap),
                onDoorFound: (door, gap) => selectedList.Remove(door.Item));
        }
        
        static partial void UpdateAllProjSpecific(float deltaTime)
        {
            var entitiesToRender = Submarine.VisibleEntities ?? mapEntityList;
            foreach (MapEntity me in entitiesToRender)
            {
                if (me is Item item)
                {
                    item.UpdateSpriteStates(deltaTime);
                }
            }
        }

        /// <summary>
        /// Draw the "selection rectangle" and outlines of entities that are being dragged (if any)
        /// </summary>
        public static void DrawSelecting(SpriteBatch spriteBatch, Camera cam)
        {
            if (GUI.MouseOn != null) return;

            Vector2 position = PlayerInput.MousePosition;
            position = cam.ScreenToWorld(position);

            if (startMovingPos != Vector2.Zero)
            {
                Vector2 moveAmount = position - startMovingPos;
                moveAmount.Y = -moveAmount.Y;
                moveAmount.X = (float)(moveAmount.X > 0.0f ? Math.Floor(moveAmount.X / Submarine.GridSize.X) : Math.Ceiling(moveAmount.X / Submarine.GridSize.X)) * Submarine.GridSize.X;
                moveAmount.Y = (float)(moveAmount.Y > 0.0f ? Math.Floor(moveAmount.Y / Submarine.GridSize.Y) : Math.Ceiling(moveAmount.Y / Submarine.GridSize.Y)) * Submarine.GridSize.Y;

                //started moving the selected entities
                if (Math.Abs(moveAmount.X) >= Submarine.GridSize.X || Math.Abs(moveAmount.Y) >= Submarine.GridSize.Y)
                {
                    foreach (MapEntity e in selectedList)
                    {
                        SpriteEffects spriteEffects = SpriteEffects.None;
                        if (e is Item item)
                        {
                            if (item.FlippedX && item.Prefab.CanSpriteFlipX) spriteEffects ^= SpriteEffects.FlipHorizontally;
                            if (item.flippedY && item.Prefab.CanSpriteFlipY) spriteEffects ^= SpriteEffects.FlipVertically;
                        }
                        else if (e is Structure structure)
                        {
                            if (structure.FlippedX && structure.Prefab.CanSpriteFlipX) spriteEffects ^= SpriteEffects.FlipHorizontally;
                            if (structure.flippedY && structure.Prefab.CanSpriteFlipY) spriteEffects ^= SpriteEffects.FlipVertically;
                        }
                        e.prefab?.DrawPlacing(spriteBatch,
                            new Rectangle(e.WorldRect.Location + new Point((int)moveAmount.X, (int)-moveAmount.Y), e.WorldRect.Size), e.Scale, spriteEffects);
                        GUI.DrawRectangle(spriteBatch,
                            new Vector2(e.WorldRect.X, -e.WorldRect.Y) + moveAmount,
                            new Vector2(e.rect.Width, e.rect.Height),
                            Color.White, false, 0, (int)Math.Max(3.0f / GameScreen.Selected.Cam.Zoom, 2.0f));
                    }

                    //stop dragging the "selection rectangle"
                    selectionPos = Vector2.Zero;
                }
            }
            if (selectionPos != null && selectionPos != Vector2.Zero)
            {
                GUI.DrawRectangle(spriteBatch, new Vector2(selectionPos.X, -selectionPos.Y), selectionSize, Color.DarkRed, false, 0, (int)Math.Max(1.5f / GameScreen.Selected.Cam.Zoom, 1.0f));
            }
        }

        public static List<MapEntity> FilteredSelectedList { get; private set; } = new List<MapEntity>();

        public static void UpdateEditor(Camera cam)
        {
            if (highlightedListBox != null) highlightedListBox.UpdateManually((float)Timing.Step);

            if (editingHUD != null)
            {
                if (FilteredSelectedList.Count == 0 || editingHUD.UserData != FilteredSelectedList[0])
                {
                    foreach (GUIComponent component in editingHUD.Children)
                    {
                        var textBox = component as GUITextBox;
                        if (textBox == null) continue;
                        textBox.Deselect();
                    }
                    editingHUD = null;
                }
            }
            FilteredSelectedList.Clear();
            if (selectedList.Count == 0) return;
            foreach (var e in selectedList)
            {
                if (e is Gap gap && gap.ConnectedDoor != null) { continue; }
                FilteredSelectedList.Add(e);
            }
            var first = FilteredSelectedList.FirstOrDefault();
            if (first != null)
            {
                first.UpdateEditing(cam);
                if (first.ResizeHorizontal || first.ResizeVertical)
                {
                    first.UpdateResizing(cam);
                }
            }

            if ((PlayerInput.KeyDown(Keys.LeftControl) || PlayerInput.KeyDown(Keys.RightControl)))
            {
                if (PlayerInput.KeyHit(Keys.N))
                {
                    float minX = selectedList[0].WorldRect.X, maxX = selectedList[0].WorldRect.Right;
                    for (int i = 0; i < selectedList.Count; i++)
                    {
                        minX = Math.Min(minX, selectedList[i].WorldRect.X);
                        maxX = Math.Max(maxX, selectedList[i].WorldRect.Right);
                    }

                    float centerX = (minX + maxX) / 2.0f;
                    foreach (MapEntity me in selectedList)
                    {
                        me.FlipX(false);
                        me.Move(new Vector2((centerX - me.WorldPosition.X) * 2.0f, 0.0f));
                    }
                }
                else if (PlayerInput.KeyHit(Keys.M))
                {
                    float minY = selectedList[0].WorldRect.Y - selectedList[0].WorldRect.Height, maxY = selectedList[0].WorldRect.Y;
                    for (int i = 0; i < selectedList.Count; i++)
                    {
                        minY = Math.Min(minY, selectedList[i].WorldRect.Y - selectedList[i].WorldRect.Height);
                        maxY = Math.Max(maxY, selectedList[i].WorldRect.Y);
                    }

                    float centerY = (minY + maxY) / 2.0f;
                    foreach (MapEntity me in selectedList)
                    {
                        me.FlipY(false);
                        me.Move(new Vector2(0.0f, (centerY - me.WorldPosition.Y) * 2.0f));
                    }
                }
            }
        }

        public static void DrawEditor(SpriteBatch spriteBatch, Camera cam)
        {
            if (selectedList.Count == 1)
            {
                selectedList[0].DrawEditing(spriteBatch, cam);

                if (selectedList[0].ResizeHorizontal || selectedList[0].ResizeVertical)
                {
                    selectedList[0].DrawResizing(spriteBatch, cam);
                }
            }

            if (highlightedListBox != null)
            {
                highlightedListBox.DrawManually(spriteBatch);
            }
        }

        public static void DeselectAll()
        {
            selectedList.Clear();
        }

        public static void SelectEntity(MapEntity entity)
        {
            DeselectAll();
            AddSelection(entity);
        }

        /// <summary>
        /// copies a list of entities to the "clipboard" (copiedList)
        /// </summary>
        public static List<MapEntity> CopyEntities(List<MapEntity> entities)
        {
            List<MapEntity> prevEntities = new List<MapEntity>(mapEntityList);

            copiedList = Clone(entities);

            //find all new entities created during cloning
            var newEntities = mapEntityList.Except(prevEntities).ToList();

            //do a "shallow remove" (removes the entities from the game without removing links between them)
            //  -> items will stay in their containers
            newEntities.ForEach(e => e.ShallowRemove());

            return newEntities;
        }

        public virtual void AddToGUIUpdateList()
        {
            if (editingHUD != null && editingHUD.UserData == this) editingHUD.AddToGUIUpdateList();
        }

        public virtual void UpdateEditing(Camera cam) { }

        protected static void PositionEditingHUD()
        {
            int maxHeight = 100;
            if (Screen.Selected == GameMain.SubEditorScreen)
            {
                editingHUD.RectTransform.SetPosition(Anchor.TopRight);
                editingHUD.RectTransform.AbsoluteOffset = new Point(0, GameMain.SubEditorScreen.TopPanel.Rect.Bottom);
                maxHeight = (GameMain.GraphicsHeight - GameMain.SubEditorScreen.EntityMenu.Rect.Height) - GameMain.SubEditorScreen.TopPanel.Rect.Bottom * 2 - 20;
            }
            else
            {
                editingHUD.RectTransform.SetPosition(Anchor.TopRight);
                editingHUD.RectTransform.RelativeOffset = new Vector2(0.0f, (HUDLayoutSettings.InventoryAreaUpper.Bottom + 10.0f) / (editingHUD.RectTransform.Parent ?? GUI.Canvas).Rect.Height);
                maxHeight = HUDLayoutSettings.InventoryAreaLower.Bottom - HUDLayoutSettings.InventoryAreaLower.Y - 10;
            }

            var listBox = editingHUD.GetChild<GUIListBox>();
            if (listBox != null)
            {
                int padding = 20;
                int contentHeight = 0;
                foreach (GUIComponent child in listBox.Content.Children)
                {
                    contentHeight += child.Rect.Height + listBox.Spacing;
                    child.RectTransform.MaxSize = new Point(int.MaxValue, child.Rect.Height);
                    child.RectTransform.MinSize = new Point(0, child.Rect.Height);
                }

                editingHUD.RectTransform.Resize(
                    new Point(
                        editingHUD.RectTransform.NonScaledSize.X, 
                        MathHelper.Clamp(contentHeight + padding * 2, 50, maxHeight)), resizeChildren: false);
                listBox.RectTransform.Resize(new Point(listBox.RectTransform.NonScaledSize.X, editingHUD.RectTransform.NonScaledSize.Y - padding * 2), resizeChildren: false);
            }
        }

        public virtual void DrawEditing(SpriteBatch spriteBatch, Camera cam) { }

        private void UpdateResizing(Camera cam)
        {
            isHighlighted = true;

            int startX = ResizeHorizontal ? -1 : 0;
            int StartY = ResizeVertical ? -1 : 0;

            for (int x = startX; x < 2; x += 2)
            {
                for (int y = StartY; y < 2; y += 2)
                {
                    Vector2 handlePos = cam.WorldToScreen(Position + new Vector2(x * (rect.Width * 0.5f + 5), y * (rect.Height * 0.5f + 5)));

                    bool highlighted = Vector2.Distance(PlayerInput.MousePosition, handlePos) < 5.0f;

                    if (highlighted && PlayerInput.LeftButtonDown())
                    {
                        selectionPos = Vector2.Zero;
                        resizeDirX = x;
                        resizeDirY = y;
                        resizing = true;
                    }
                }
            }

            if (resizing)
            {
                if (rectMemento == null)
                {
                    rectMemento = new Memento<Rectangle>();
                    rectMemento.Store(Rect);
                }

                Vector2 placePosition = new Vector2(rect.X, rect.Y);
                Vector2 placeSize = new Vector2(rect.Width, rect.Height);

                Vector2 mousePos = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (resizeDirX > 0)
                {
                    mousePos.X = Math.Max(mousePos.X, rect.X + Submarine.GridSize.X);
                    placeSize.X = mousePos.X - placePosition.X;
                }
                else if (resizeDirX < 0)
                {
                    mousePos.X = Math.Min(mousePos.X, rect.Right - Submarine.GridSize.X);

                    placeSize.X = (placePosition.X + placeSize.X) - mousePos.X;
                    placePosition.X = mousePos.X;
                }
                if (resizeDirY < 0)
                {
                    mousePos.Y = Math.Min(mousePos.Y, rect.Y - Submarine.GridSize.Y);
                    placeSize.Y = placePosition.Y - mousePos.Y;
                }
                else if (resizeDirY > 0)
                {
                    mousePos.Y = Math.Max(mousePos.Y, rect.Y - rect.Height + Submarine.GridSize.X);

                    placeSize.Y = mousePos.Y - (rect.Y - rect.Height);
                    placePosition.Y = mousePos.Y;
                }

                if ((int)placePosition.X != rect.X || (int)placePosition.Y != rect.Y || (int)placeSize.X != rect.Width || (int)placeSize.Y != rect.Height)
                {
                    Rect = new Rectangle((int)placePosition.X, (int)placePosition.Y, (int)placeSize.X, (int)placeSize.Y);
                }

                if (!PlayerInput.LeftButtonHeld())
                {
                    rectMemento.Store(Rect);
                    resizing = false;
                }
            }
        }

        private void DrawResizing(SpriteBatch spriteBatch, Camera cam)
        {
            isHighlighted = true;

            int startX = ResizeHorizontal ? -1 : 0;
            int StartY = ResizeVertical ? -1 : 0;

            for (int x = startX; x < 2; x += 2)
            {
                for (int y = StartY; y < 2; y += 2)
                {
                    Vector2 handlePos = cam.WorldToScreen(Position + new Vector2(x * (rect.Width * 0.5f + 5), y * (rect.Height * 0.5f + 5)));

                    bool highlighted = Vector2.Distance(PlayerInput.MousePosition, handlePos) < 5.0f;

                    GUI.DrawRectangle(spriteBatch,
                        handlePos - new Vector2(3.0f, 3.0f),
                        new Vector2(6.0f, 6.0f),
                        Color.White * (highlighted ? 1.0f : 0.6f),
                        true, 0,
                        (int)Math.Max(1.5f / GameScreen.Selected.Cam.Zoom, 1.0f));
                }
            }
        }

        /// <summary>
        /// Find entities whose rect intersects with the "selection rect"
        /// </summary>
        public static List<MapEntity> FindSelectedEntities(Vector2 pos, Vector2 size)
        {
            List<MapEntity> foundEntities = new List<MapEntity>();

            Rectangle selectionRect = Submarine.AbsRect(pos, size);

            foreach (MapEntity e in mapEntityList)
            {
                if (!e.SelectableInEditor) continue;

                if (Submarine.RectsOverlap(selectionRect, e.rect)) foundEntities.Add(e);
            }

            return foundEntities;
        }
    }
}
