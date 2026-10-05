namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The Armada focus router (TUIKit gap U1: TabView, SplitView, and ScrollView do not forward keys to children). A
    /// scope owns an ordered set of child widgets, routes keys to the focused child first, moves focus with
    /// <c>Tab</c>/<c>Shift+Tab</c> (bubbling to the parent scope at either end unless <see cref="Wrap"/> is set),
    /// hit-tests mouse presses against the rectangles recorded when children were placed, and keeps
    /// <see cref="IFocusAware"/> state in sync. Scopes nest: a child may itself be a scope. Not thread-safe.
    /// </summary>
    public class FocusScope
    {
        #region Public-Members

        /// <summary>
        /// Wrap Tab traversal at the ends instead of bubbling. Default false.
        /// </summary>
        public bool Wrap { get; set; } = false;

        /// <summary>
        /// Children in focus order. Never null.
        /// </summary>
        public IReadOnlyList<IWidget> Children
        {
            get { return _Children; }
        }

        /// <summary>
        /// Focused child, or null.
        /// </summary>
        public IWidget? Focused
        {
            get { return _Index >= 0 && _Index < _Children.Count ? _Children[_Index] : null; }
        }

        /// <summary>
        /// Index of the focused child, or -1.
        /// </summary>
        public int FocusedIndex
        {
            get { return _Index; }
        }

        /// <summary>
        /// True while the scope itself holds focus (its owner is focused).
        /// </summary>
        public bool IsActive { get; private set; } = false;

        /// <summary>
        /// True when this scope's children are the focus regions of a pane (screens set it), so
        /// <see cref="FocusedRegion"/> descends into it to find the focused sub-region. Default false.
        /// </summary>
        public bool RegionHost { get; set; } = false;

        /// <summary>
        /// Raised after the focused child changes.
        /// </summary>
        public event EventHandler<IWidget?>? FocusMoved;

        #endregion

        #region Private-Members

        private readonly List<IWidget> _Children = new List<IWidget>();
        private readonly Dictionary<IWidget, Rect> _Rects = new Dictionary<IWidget, Rect>();
        private int _Index = -1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a child at the end of the focus order.
        /// </summary>
        /// <param name="child">Child widget.</param>
        /// <returns>The child.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="child"/> is null.</exception>
        public T Add<T>(T child) where T : IWidget
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            _Children.Add(child);
            if (_Index < 0 && CanTakeFocus(child)) _Index = _Children.Count - 1;
            return child;
        }

        /// <summary>
        /// Remove every child.
        /// </summary>
        public void Clear()
        {
            if (IsActive) Notify(Focused, false);
            _Children.Clear();
            _Rects.Clear();
            _Index = -1;
        }

        /// <summary>
        /// Remove a child.
        /// </summary>
        /// <param name="child">Child.</param>
        public void Remove(IWidget child)
        {
            int idx = _Children.IndexOf(child);
            if (idx < 0) return;
            bool wasFocused = idx == _Index;
            if (wasFocused && IsActive) Notify(child, false);
            _Children.RemoveAt(idx);
            _Rects.Remove(child);
            if (_Index >= _Children.Count || wasFocused) _Index = FirstFocusable();
            else if (idx < _Index) _Index--;
            if (wasFocused && IsActive) Notify(Focused, true);
        }

        /// <summary>
        /// Record where a child was drawn (in the owner's coordinates) so mouse presses can be routed to it.
        /// </summary>
        /// <param name="child">Child.</param>
        /// <param name="rect">Rectangle.</param>
        public void Place(IWidget child, Rect rect)
        {
            if (child == null) return;
            _Rects[child] = rect;
        }

        /// <summary>
        /// Render a child into a rectangle of the owner's surface and record the rectangle.
        /// </summary>
        /// <param name="surface">Owner surface.</param>
        /// <param name="child">Child.</param>
        /// <param name="rect">Rectangle.</param>
        public void RenderChild(ISurface surface, IWidget child, Rect rect)
        {
            if (surface == null || child == null) return;
            Rect clipped = rect.Intersect(new Rect(0, 0, surface.Size.Width, surface.Size.Height));
            if (clipped.IsEmpty)
            {
                _Rects.Remove(child);
                return;
            }

            Place(child, clipped);
            child.Render(new SurfaceView(surface, clipped));
        }

        /// <summary>
        /// The rectangle a child was last placed in, or an empty rectangle.
        /// </summary>
        /// <param name="child">Child.</param>
        /// <returns>Rectangle.</returns>
        public Rect RectOf(IWidget child)
        {
            return child != null && _Rects.TryGetValue(child, out Rect r) ? r : Rect.Empty;
        }

        /// <summary>
        /// Focus a specific child.
        /// </summary>
        /// <param name="child">Child.</param>
        /// <returns>True when focus moved.</returns>
        public bool Focus(IWidget child)
        {
            int idx = _Children.IndexOf(child);
            if (idx < 0 || !CanTakeFocus(child)) return false;
            SetIndex(idx);
            return true;
        }

        /// <summary>
        /// Focus the first focusable child.
        /// </summary>
        /// <returns>True when a child was focused.</returns>
        public bool FocusFirst()
        {
            int idx = FirstFocusable();
            if (idx < 0) return false;
            SetIndex(idx);
            return true;
        }

        /// <summary>
        /// Focus the last focusable child.
        /// </summary>
        /// <returns>True when a child was focused.</returns>
        public bool FocusLast()
        {
            for (int i = _Children.Count - 1; i >= 0; i--)
            {
                if (CanTakeFocus(_Children[i]))
                {
                    SetIndex(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Move focus forward or backward; returns false at a boundary when <see cref="Wrap"/> is off.
        /// </summary>
        /// <param name="forward">Direction.</param>
        /// <returns>True when focus moved.</returns>
        public bool Move(bool forward)
        {
            if (_Children.Count == 0) return false;
            int i = _Index;
            for (int step = 0; step < _Children.Count; step++)
            {
                i += forward ? 1 : -1;
                if (i >= _Children.Count || i < 0)
                {
                    if (!Wrap) return false;
                    i = forward ? 0 : _Children.Count - 1;
                }

                if (CanTakeFocus(_Children[i]))
                {
                    if (i == _Index) return false;
                    SetIndex(i);
                    if (_Children[i] is IFocusScopeOwner owner)
                    {
                        if (forward) owner.Scope.FocusFirst();
                        else owner.Scope.FocusLast();
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Activate or deactivate the scope (the owner gained or lost focus); notifies the focused child.
        /// </summary>
        /// <param name="active">Active state.</param>
        public void SetActive(bool active)
        {
            if (IsActive == active) return;
            IsActive = active;
            if (_Index < 0) _Index = FirstFocusable();
            Notify(Focused, active);
        }

        /// <summary>
        /// Route a key: the focused child first, then Tab traversal.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when consumed.</returns>
        public bool HandleKey(KeyEvent key)
        {
            IWidget? focused = Focused;
            if (focused is IFocusable f && f.HandleKey(key)) return true;
            if (key.Code == KeyCode.Tab && (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
            {
                bool backward = (key.Modifiers & KeyModifiers.Shift) != 0;
                return Move(!backward);
            }

            return false;
        }

        /// <summary>
        /// Route a mouse event in owner coordinates: a press focuses the child under the pointer; the event is
        /// forwarded in child coordinates to <see cref="IMouseAware"/> children.
        /// </summary>
        /// <param name="mouse">Mouse event.</param>
        /// <returns>True when consumed.</returns>
        public bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null) return false;
            for (int i = _Children.Count - 1; i >= 0; i--)
            {
                IWidget child = _Children[i];
                if (!_Rects.TryGetValue(child, out Rect r) || !r.Contains(new Point(mouse.X, mouse.Y))) continue;
                if (child is ArmadaWidget aw && !aw.Visible) continue;
                if (mouse.Kind == MouseEventKind.Press && CanTakeFocus(child)) SetIndex(i);
                if (child is IMouseAware aware)
                {
                    MouseEvent local = new MouseEvent(mouse.Kind, mouse.Button, mouse.X - r.X, mouse.Y - r.Y, mouse.Modifiers, mouse.ClickCount);
                    return aware.HandleMouse(local);
                }

                return mouse.Kind == MouseEventKind.Press;
            }

            return false;
        }

        /// <summary>
        /// The rectangle of the focused sub-region in the owner's coordinates: the focused child's last placement,
        /// descending through children whose scope is a <see cref="RegionHost"/> (a hub's content screen), so the result
        /// is the deepest region that holds focus. Used to draw the pane border alongside it (see
        /// <see cref="FocusFrame"/>).
        /// </summary>
        /// <returns>The rectangle, or <see cref="Rect.Empty"/> when no focused child has been placed.</returns>
        public Rect FocusedRegion()
        {
            Rect result = Rect.Empty;
            FocusScope scope = this;
            int ox = 0;
            int oy = 0;
            for (int depth = 0; depth < 16; depth++)
            {
                IWidget? child = scope.Focused;
                if (child == null) break;
                Rect r = scope.RectOf(child);
                if (r.IsEmpty) break;
                result = new Rect(r.X + ox, r.Y + oy, r.Width, r.Height);
                if (!(child is IFocusScopeOwner owner) || !owner.Scope.RegionHost) break;
                scope = owner.Scope;
                ox = result.X;
                oy = result.Y;
            }

            return result;
        }

        /// <summary>
        /// The deepest focused widget (following nested scopes).
        /// </summary>
        /// <returns>The leaf, or null.</returns>
        public IWidget? FocusedLeaf()
        {
            IWidget? focused = Focused;
            if (focused is IFocusScopeOwner owner)
            {
                IWidget? inner = owner.Scope.FocusedLeaf();
                return inner ?? focused;
            }

            return focused;
        }

        #endregion

        #region Private-Methods

        private void SetIndex(int idx)
        {
            if (idx == _Index) return;
            IWidget? previous = Focused;
            _Index = idx;
            if (IsActive)
            {
                Notify(previous, false);
                Notify(Focused, true);
            }

            EventHandler<IWidget?>? handler = FocusMoved;
            if (handler != null) handler(this, Focused);
        }

        private int FirstFocusable()
        {
            for (int i = 0; i < _Children.Count; i++)
            {
                if (CanTakeFocus(_Children[i])) return i;
            }

            return -1;
        }

        private static bool CanTakeFocus(IWidget child)
        {
            if (!(child is IFocusable)) return false;
            if (child is ArmadaWidget aw) return aw.Visible && aw.CanFocus;
            return true;
        }

        private static void Notify(IWidget? widget, bool focused)
        {
            if (widget is IFocusAware aware) aware.OnFocusChanged(focused);
        }

        #endregion
    }
}
