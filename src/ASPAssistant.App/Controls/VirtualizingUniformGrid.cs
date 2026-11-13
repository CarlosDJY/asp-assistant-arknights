// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ASPAssistant.App.Controls;

/// <summary>
/// A virtualizing grid of equal cells (<see cref="Columns"/> per row, rows <see cref="ItemHeight"/> tall): only the rows in
/// view get containers, so a picker with a few hundred cards opens at once. Used as a ListBox items panel
/// (the ListBox's ScrollViewer must keep CanContentScroll on, which is the default).
/// </summary>
public sealed class VirtualizingUniformGrid : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(VirtualizingUniformGrid), new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingUniformGrid), new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    /// <summary>Row height including the containers' margins.</summary>
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }

    private Size _extent;
    private Size _viewport;
    private Point _offset;

    private int ItemCount => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
    private int Cols => Math.Max(1, Columns);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width;
        var count = ItemCount;
        var rows = (count + Cols - 1) / Cols;
        var viewportHeight = double.IsInfinity(availableSize.Height) ? rows * ItemHeight : availableSize.Height;
        UpdateScrollInfo(new Size(width, rows * ItemHeight), new Size(width, viewportHeight));

        var first = Math.Max(0, (int)Math.Floor(_offset.Y / ItemHeight) * Cols);
        var last = Math.Min(count - 1, ((int)Math.Ceiling((_offset.Y + viewportHeight) / ItemHeight) + 1) * Cols - 1);
        var cell = new Size(width / Cols, ItemHeight);

        // Touch ItemContainerGenerator once so InternalChildren is backed by the generator.
        _ = InternalChildren;
        var generator = ItemContainerGenerator;
        if (count > 0 && last >= first)
        {
            var start = generator.GeneratorPositionFromIndex(first);
            var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
            using (generator.StartAt(start, GeneratorDirection.Forward, true))
            {
                for (var i = first; i <= last; i++, childIndex++)
                {
                    if (generator.GenerateNext(out var isNew) is not UIElement child) continue;
                    if (isNew)
                    {
                        if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                        else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(cell);
                }
            }
        }
        CleanUp(first, last);
        return new Size(width, viewportHeight);
    }

    private void CleanUp(int first, int last)
    {
        var generator = ItemContainerGenerator;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index < first || index > last)
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        var cellWidth = finalSize.Width / Cols;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0) continue;
            InternalChildren[i].Arrange(new Rect(index % Cols * cellWidth, index / Cols * ItemHeight - _offset.Y, cellWidth, ItemHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                // A new filter: back to the top.
                _offset = new Point();
                break;
        }
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        var top = index / Cols * ItemHeight;
        if (top < _offset.Y) SetVerticalOffset(top);
        else if (top + ItemHeight > _offset.Y + _viewport.Height) SetVerticalOffset(top + ItemHeight - _viewport.Height);
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        var changed = extent != _extent || viewport != _viewport;
        _extent = extent;
        _viewport = viewport;
        var max = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset.Y > max) { _offset.Y = max; changed = true; }
        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    // ---- IScrollInfo ----
    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(_offset.Y - ItemHeight / 2);
    public void LineDown() => SetVerticalOffset(_offset.Y + ItemHeight / 2);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - ItemHeight);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + ItemHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, _extent.Height - _viewport.Height));
        if (Math.Abs(offset - _offset.Y) < 0.01) return;
        _offset.Y = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement element)
        {
            var i = InternalChildren.IndexOf(element);
            if (i < 0 && VisualTreeHelper.GetParent(visual) is UIElement)
                for (var v = visual as DependencyObject; v is not null && v != this; v = VisualTreeHelper.GetParent(v))
                    if (v is UIElement u && InternalChildren.IndexOf(u) is var j and >= 0) { i = j; break; }
            if (i >= 0)
            {
                var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                if (index >= 0) BringIndexIntoView(index);
            }
        }
        return rectangle;
    }
}
