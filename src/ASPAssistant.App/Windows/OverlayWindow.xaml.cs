// Modified for ASP Assistant Regulation on 2026-11-11; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ASPAssistant.Core;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class OverlayWindow : Window
{
    private const double MarkerSize = 27;
    private const double BanMarkerSize = 30;

    // Persistent marker elements keyed by ShopItem.Id.
    // Using Id (not Name) lets multiple cards with the same operator name each
    // have their own independent marker.
    private readonly Dictionary<string, FrameworkElement> _markerPool = [];
    private IReadOnlyList<ShopItem> _shopItems = [];
    private (double Width, double Height) _markerViewport;

    // Ban-screen avatar markers keyed by operator name.
    private readonly List<FrameworkElement> _banMarkerPool = [];

    // The full-screen instruction layer shown at the start of ban detection.
    private FrameworkElement? _banInstructionLayer;
    private FrameworkElement? _banInputBlockLayer;
    private FrameworkElement? _banTimedMessageLayer;

    // Debug card border overlays, keyed by ShopItem.Id.
    private readonly Dictionary<string, FrameworkElement> _debugBorderPool = [];

    /// <summary>
    /// When <see langword="true"/>, <see cref="UpdateShopItems"/> draws a
    /// semi-transparent border around every detected operator card boundary
    /// (green = tracked, orange = detected but not tracked). Toggle at
    /// runtime to aid alignment debugging without restarting the app.
    /// </summary>
    public bool ShowDebugCardBorders { get; set; } = false;
    public Func<IntPtr> FindGameWindow { get; init; } = User32.FindArknightsWindow;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) =>
        {
            UpdateShopItems(_shopItems);
            DrawBoard();
            foreach (var layer in new[] { _banInstructionLayer, _banInputBlockLayer, _banTimedMessageLayer })
            {
                if (layer is null) continue;
                layer.Width = ActualWidth;
                layer.Height = ActualHeight;
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
            exStyle | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE);
    }

    // ── Deployment board ─────────────────────────────────────────────────────

    private readonly Canvas _boardLayer = new() { IsHitTestVisible = false };
    private (GarrisonMap Map, BoardProjection Projection)? _board;

    private static readonly SolidColorBrush GroundBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xD0, 0x2E, 0xCC, 0x71)));
    private static readonly SolidColorBrush SpecialBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xD0, 0x2E, 0xD1, 0xB4)));
    private static readonly SolidColorBrush HighBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE0, 0x4C, 0x8D, 0xFF)));
    private static readonly SolidColorBrush RouteBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0x5A, 0x3C)));

    /// <summary>
    /// Shows the recognised board's deployable cells (outline colour = ground / special / high ground) and the
    /// enemy routes; null hides it. Only outlines, a corner glyph and route lines through cell centres are drawn,
    /// so the colour samples BoardRecognizer reads from the captured screen stay untouched.
    /// </summary>
    public void UpdateBoard(GarrisonMap? map, BoardProjection? projection, bool showCells = true)
    {
        _board = map is null || projection is null ? null : (map, projection);
        _showCells = showCells;
        DrawBoard();
    }

    private bool _showCells = true;
    private PlacementPlan? _placements;

    /// <summary>
    /// Planned positions to draw on the recognised board (null hides them). Drawn only when the plan is for the
    /// map on screen. Labels sit across the cell centre and the facing mark on the edge, clear of the sample points.
    /// </summary>
    public void UpdatePlacements(PlacementPlan? plan)
    {
        _placements = plan;
        DrawBoard();
    }

    private static readonly SolidColorBrush LabelBackground = Freeze(new SolidColorBrush(Color.FromArgb(0xD9, 0x0A, 0x0C, 0x10)));
    private static readonly SolidColorBrush PlaceDamageBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)));
    private static readonly SolidColorBrush PlaceTankBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));
    private static readonly SolidColorBrush PlaceSupportBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)));
    private static readonly SolidColorBrush PlaceStackerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC7, 0x7D, 0xFF)));

    private void DrawPlacements(GarrisonMap map, Func<double, double, Point> at, double w)
    {
        if (_placements is not { } plan || plan.Map.Id != map.Id) return;
        foreach (var p in plan.Placed)
        {
            var brush = p.Role switch
            {
                PlacementRole.Damage => PlaceDamageBrush,
                PlacementRole.Tank => PlaceTankBrush,
                PlacementRole.Support => PlaceSupportBrush,
                _ => PlaceStackerBrush,
            };
            const double inset = 0.08;
            int c = p.Cell.Col, r = p.Cell.Row;
            // Cell frame in the role colour (no fill, so the board colours the recognizer samples stay visible).
            _boardLayer.Children.Add(new Polygon
            {
                Points = [at(c + inset, r + inset), at(c + 1 - inset, r + inset), at(c + 1 - inset, r + 1 - inset), at(c + inset, r + 1 - inset)],
                Stroke = brush,
                StrokeThickness = 3,
                IsHitTestVisible = false,
            });
            // Facing: a triangle pointing out of the edge the operator faces.
            var (fx, fy) = PlacementCells.Vector(p.Facing);
            var tip = at(c + 0.5 + fx * 0.46, r + 0.5 + fy * 0.46);
            var b1 = at(c + 0.5 + fx * 0.3 - fy * 0.13, r + 0.5 + fy * 0.3 - fx * 0.13);
            var b2 = at(c + 0.5 + fx * 0.3 + fy * 0.13, r + 0.5 + fy * 0.3 + fx * 0.13);
            _boardLayer.Children.Add(new Polygon { Points = [tip, b1, b2], Fill = brush, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false });
            // Name tag near the top of the cell: dark plate with a role-colour border.
            var tag = new Border
            {
                Background = LabelBackground,
                BorderBrush = brush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = p.Name,
                    Foreground = Brushes.White,
                    FontSize = Math.Clamp(w / 92, 10, 16),
                    FontWeight = FontWeights.Bold,
                },
            };
            tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var anchor = at(c + 0.5, r + 0.16);
            Canvas.SetLeft(tag, anchor.X - tag.DesiredSize.Width / 2);
            Canvas.SetTop(tag, anchor.Y - tag.DesiredSize.Height / 2);
            _boardLayer.Children.Add(tag);
        }
    }

    private void DrawBoard()
    {
        if (!OverlayCanvas.Children.Contains(_boardLayer))
            OverlayCanvas.Children.Insert(0, _boardLayer);
        _boardLayer.Children.Clear();
        if (_board is not { } board || ActualWidth <= 0 || ActualHeight <= 0) return;
        var (map, projection) = board;
        double w = ActualWidth, h = ActualHeight;
        Point At(double col, double row)
        {
            var (x, y) = projection.Project(col, row);
            return new Point(x * w, y * h);
        }

        foreach (var (cell, type) in _showCells ? map.AllCells() : Enumerable.Empty<(BoardCell Cell, BoardCellType Type)>())
        {
            var brush = type switch
            {
                BoardCellType.Ground => GroundBrush,
                BoardCellType.Special => SpecialBrush,
                BoardCellType.High => HighBrush,
                _ => null,
            };
            if (brush is null) continue;
            const double inset = 0.06;
            var outline = new Polygon
            {
                Points = [At(cell.Col + inset, cell.Row + inset), At(cell.Col + 1 - inset, cell.Row + inset),
                          At(cell.Col + 1 - inset, cell.Row + 1 - inset), At(cell.Col + inset, cell.Row + 1 - inset)],
                Stroke = brush,
                StrokeThickness = type == BoardCellType.High ? 2.2 : 1.6,
                StrokeDashArray = type == BoardCellType.Special ? [3, 2] : null,
                IsHitTestVisible = false,
            };
            _boardLayer.Children.Add(outline);
            var glyph = new TextBlock
            {
                Text = type switch { BoardCellType.High => "高", BoardCellType.Special => "特", _ => "地" },
                Foreground = brush,
                FontSize = Math.Clamp(w / 110, 9, 15),
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9 },
            };
            var p = At(cell.Col + 0.12, cell.Row + 0.1);
            Canvas.SetLeft(glyph, p.X);
            Canvas.SetTop(glyph, p.Y);
            _boardLayer.Children.Add(glyph);
        }

        foreach (var route in _showCells ? map.Routes : Array.Empty<IReadOnlyList<BoardCell>>())
        {
            if (route.Count < 2) continue;
            var line = new Polyline
            {
                Points = [.. route.Select(c => At(c.Col + 0.5, c.Row + 0.5))],
                Stroke = RouteBrush,
                StrokeThickness = 2.4,
                StrokeDashArray = [4, 2],
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            _boardLayer.Children.Add(line);
            // Arrow head at the defense point.
            var end = At(route[^1].Col + 0.5, route[^1].Row + 0.5);
            var prev = At(route[^2].Col + 0.5, route[^2].Row + 0.5);
            var dir = end - prev;
            if (dir.Length > 0)
            {
                dir.Normalize();
                var normal = new Vector(-dir.Y, dir.X);
                double size = Math.Clamp(w / 140, 6, 12);
                _boardLayer.Children.Add(new Polygon
                {
                    Points = [end, end - dir * size * 1.6 + normal * size, end - dir * size * 1.6 - normal * size],
                    Fill = RouteBrush,
                    IsHitTestVisible = false,
                });
            }
        }
        DrawPlacements(map, At, w);
    }

    public void UpdatePosition(RECT gameClientRect)
    {
        User32.SetWindowBounds(new WindowInteropHelper(this).EnsureHandle(), gameClientRect);
    }

    /// <summary>
    /// Synchronises the overlay with the current shop state.
    /// New tracked items fade in; existing ones are repositioned without
    /// recreation; items no longer tracked fade out and are released.
    /// </summary>
    public void UpdateShopItems(IReadOnlyList<ShopItem> shopItems)
    {
        _shopItems = shopItems;
        var sameViewport = _markerViewport == (ActualWidth, ActualHeight);
        _markerViewport = (ActualWidth, ActualHeight);
        var nowTracked = shopItems
            .Where(s => s.IsTracked)
            .ToDictionary(s => s.Id);

        AppLogger.Info("Overlay",
            $"UpdateShopItems: total={shopItems.Count} tracked={nowTracked.Count} " +
            $"overlay={ActualWidth:F0}x{ActualHeight:F0} " +
            $"items=[{string.Join(", ", shopItems.Select(i => $"{i.Name}(tracked={i.IsTracked},region=({i.OcrRegion.X:F3},{i.OcrRegion.Y:F3},{i.OcrRegion.Width:F3},{i.OcrRegion.Height:F3}))"))}]");

        // Fade out markers whose card slots are no longer present
        var toRemove = _markerPool.Keys.Except(nowTracked.Keys).ToList();
        if (toRemove.Count > 0)
            AppLogger.Info("Overlay", $"Fading out {toRemove.Count} marker(s): [{string.Join(", ", toRemove)}]");
        foreach (var id in toRemove)
            FadeOutAndRemove(id);

        // Add new markers or reposition existing ones
        foreach (var item in nowTracked.Values)
        {
            var (mx, my) = WindowGeometry.ShopMarkerPosition(item.OcrRegion, ActualWidth, ActualHeight, MarkerSize);

            if (_markerPool.TryGetValue(item.Id, out var existing))
            {
                SetMarkerColor(existing, item.IsTransition);
                if (sameViewport && Math.Abs(Canvas.GetLeft(existing) - mx) < 6 && Math.Abs(Canvas.GetTop(existing) - my) < 6) continue;
                Canvas.SetLeft(existing, mx);
                Canvas.SetTop(existing, my);
                AppLogger.Info("Overlay", $"Repositioned marker '{item.Id}' to ({mx:F1},{my:F1})");
            }
            else
            {
                var marker = CreateMarker();
                SetMarkerColor(marker, item.IsTransition);
                marker.Opacity = 0;
                Canvas.SetLeft(marker, mx);
                Canvas.SetTop(marker, my);
                OverlayCanvas.Children.Add(marker);
                _markerPool[item.Id] = marker;

                AppLogger.Info("Overlay", $"Added new marker '{item.Id}' at ({mx:F1},{my:F1})");
                marker.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            }
        }

        if (ShowDebugCardBorders)
            UpdateDebugBorders(shopItems);
    }

    // ── Debug card borders ────────────────────────────────────────────────────

    /// <summary>
    /// Redraws debug border rectangles for every detected card.
    /// Green border + name label = tracked; orange border = detected, not tracked.
    /// Clears and re-creates on every call so positions always stay in sync.
    /// </summary>
    private void UpdateDebugBorders(IReadOnlyList<ShopItem> shopItems)
    {
        // Remove borders for cards no longer in the set.
        var currentIds = shopItems.Select(i => i.Id).ToHashSet();
        foreach (var id in _debugBorderPool.Keys.Except(currentIds).ToList())
        {
            OverlayCanvas.Children.Remove(_debugBorderPool[id]);
            _debugBorderPool.Remove(id);
        }

        foreach (var item in shopItems)
        {
            double bx = item.OcrRegion.X      * ActualWidth;
            double by = item.OcrRegion.Y      * ActualHeight;
            double bw = item.OcrRegion.Width  * ActualWidth;
            double bh = item.OcrRegion.Height * ActualHeight;

            if (_debugBorderPool.TryGetValue(item.Id, out var existing))
            {
                Canvas.SetLeft(existing, bx);
                Canvas.SetTop(existing, by);
                if (existing is Grid g)
                {
                    g.Width  = bw;
                    g.Height = bh;
                    if (g.Children[0] is Border b)
                        b.BorderBrush = item.IsTracked
                            ? new SolidColorBrush(Color.FromArgb(0xCC, 0x4C, 0xAF, 0x50))
                            : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x98, 0x00));
                }
            }
            else
            {
                var border = CreateDebugBorder(item, bw, bh);
                Canvas.SetLeft(border, bx);
                Canvas.SetTop(border, by);
                OverlayCanvas.Children.Add(border);
                _debugBorderPool[item.Id] = border;
            }
        }
    }

    private static Grid CreateDebugBorder(ShopItem item, double w, double h)
    {
        var color = item.IsTracked
            ? Color.FromArgb(0xCC, 0x4C, 0xAF, 0x50)   // green
            : Color.FromArgb(0xCC, 0xFF, 0x98, 0x00);   // orange

        var border = new Border
        {
            Width            = w,
            Height           = h,
            BorderBrush      = new SolidColorBrush(color),
            BorderThickness  = new Thickness(2),
            Background       = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B)),
            IsHitTestVisible = false,
        };

        var label = new TextBlock
        {
            Text             = item.Name,
            Foreground       = Brushes.White,
            FontSize         = 11,
            FontWeight       = FontWeights.SemiBold,
            Margin           = new Thickness(3, 1, 3, 1),
            IsHitTestVisible = false,
            Effect           = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9
            }
        };

        var grid = new Grid { Width = w, Height = h, IsHitTestVisible = false };
        grid.Children.Add(border);
        grid.Children.Add(label);
        return grid;
    }

    private void FadeOutAndRemove(string id)
    {
        var el = _markerPool[id];
        _markerPool.Remove(id);

        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
        anim.Completed += (_, _) =>
        {
            OverlayCanvas.Children.Remove(el);
        };
        el.BeginAnimation(OpacityProperty, anim);
    }

    private static readonly SolidColorBrush FinalMarkerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)));
    private static readonly SolidColorBrush TransitionMarkerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)));

    private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

    /// <summary>Green = 大成 (final) operator, orange = 过渡 (transition) operator of an imported route.</summary>
    private static void SetMarkerColor(FrameworkElement marker, bool transition)
    {
        if (marker is Panel panel && panel.Children.OfType<Ellipse>().FirstOrDefault() is { } ellipse)
            ellipse.Fill = transition ? TransitionMarkerBrush : FinalMarkerBrush;
    }

    private static Grid CreateMarker()
    {
        var grid = new Grid
        {
            Width = MarkerSize,
            Height = MarkerSize
        };

        var ellipse = new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                ShadowDepth = 2,
                Opacity = 0.4
            }
        };

        var star = new TextBlock
        {
            Text = "\u2605",   // ?
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(ellipse);
        grid.Children.Add(star);
        return grid;
    }

    // ?? Ban instruction overlay ???????????????????????????????????????????????

    /// <summary>
    /// Displays a semi-transparent grey overlay on the game window with an
    /// animated downward arrow prompting the player to scroll. The overlay
    /// intercepts the first scroll wheel event, dismisses itself, and
    /// forwards the scroll to the game window so the player's input is not lost.
    /// </summary>
    public void ShowBanInstructionOverlay()
    {
        // Remove any pre-existing layer (e.g. rapid re-entry).
        HideBanInstructionOverlay();

        // Full-screen dimming rectangle.
        var dimRect = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(dimRect, 0);
        Canvas.SetTop(dimRect, 0);
        dimRect.Width = ActualWidth > 0 ? ActualWidth : 1280;
        dimRect.Height = ActualHeight > 0 ? ActualHeight : 720;

        // Central message panel.
        var mainText = new TextBlock
        {
            Text = "下滑后按 Ctrl+Shift+/ 截取ban位记录",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.8
            }
        };

        var hintText = new TextBlock
        {
            Text = "(尽量保持完整的干员头像)",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.7
            }
        };

        // Downward-pointing arrow drawn as a Polygon, animated to bounce vertically.
        var arrowTransform = new TranslateTransform();
        var arrowAnim = new DoubleAnimation(0, 12,
            new Duration(TimeSpan.FromSeconds(0.6)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        arrowTransform.BeginAnimation(TranslateTransform.YProperty, arrowAnim);

        var arrow = new Polygon
        {
            Points = new PointCollection { new(0, 0), new(28, 0), new(14, 18) },
            Fill = Brushes.White,
            RenderTransform = arrowTransform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.6
            }
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(mainText);
        panel.Children.Add(new Border { Height = 4 });
        panel.Children.Add(hintText);
        panel.Children.Add(new Border { Height = 12 });
        panel.Children.Add(arrow);

        // Wrap dim rect and panel into a single container for unified fade-out.
        var container = new Canvas { IsHitTestVisible = true };
        container.Children.Add(dimRect);

        panel.Loaded += (_, _) =>
        {
            Canvas.SetLeft(panel, (container.ActualWidth - panel.ActualWidth) / 2);
            Canvas.SetTop(panel, (container.ActualHeight - panel.ActualHeight) / 2);
        };
        container.Children.Add(panel);

        container.SizeChanged += (_, e) =>
        {
            dimRect.Width = e.NewSize.Width;
            dimRect.Height = e.NewSize.Height;
            Canvas.SetLeft(panel, (e.NewSize.Width - panel.ActualWidth) / 2);
            Canvas.SetTop(panel, (e.NewSize.Height - panel.ActualHeight) / 2);
        };

        Canvas.SetLeft(container, 0);
        Canvas.SetTop(container, 0);
        container.Width = ActualWidth > 0 ? ActualWidth : 1280;
        container.Height = ActualHeight > 0 ? ActualHeight : 720;

        container.Opacity = 0;
        OverlayCanvas.Children.Add(container);
        _banInstructionLayer = container;

        container.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        // Remove click-through so we can receive the scroll wheel event.
        UpdateClickThroughMode();
        MouseWheel += OnBanScreenScrollDetected;
    }

    /// <summary>
    /// Hides the ban instruction overlay. Safe to call even if it was already
    /// hidden (e.g. dismissed by the player's scroll wheel earlier).
    /// </summary>
    public void HideBanInstructionOverlay()
    {
        MouseWheel -= OnBanScreenScrollDetected;

        if (_banInstructionLayer == null)
        {
            UpdateClickThroughMode();
            return;
        }

        var layer = _banInstructionLayer;
        _banInstructionLayer = null;

        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        anim.Completed += (_, _) => OverlayCanvas.Children.Remove(layer);
        layer.BeginAnimation(OpacityProperty, anim);
        UpdateClickThroughMode();
    }

    private void OnBanScreenScrollDetected(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        HideBanInstructionOverlay();

        // Forward the scroll to the game window so the player's first scroll is not swallowed.
        var gameHwnd = FindGameWindow();
        if (gameHwnd != IntPtr.Zero)
        {
            User32.GetCursorPos(out var cursorPos);
            var wParam = (IntPtr)((e.Delta << 16) & 0xFFFF0000);
            var lParam = (IntPtr)(((cursorPos.Y & 0xFFFF) << 16) | (cursorPos.X & 0xFFFF));
            User32.SendMessage(gameHwnd, User32.WM_MOUSEWHEEL, wParam, lParam);
        }
    }

    /// <summary>
    /// Shows a transparent full-screen blocker that intercepts mouse input on the game window.
    /// </summary>
    public void ShowBanInputBlockOverlay()
    {
        HideBanInputBlockOverlay();

        var blocker = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            IsHitTestVisible = true
        };
        Canvas.SetLeft(blocker, 0);
        Canvas.SetTop(blocker, 0);
        blocker.Width = ActualWidth > 0 ? ActualWidth : 1280;
        blocker.Height = ActualHeight > 0 ? ActualHeight : 720;

        OverlayCanvas.Children.Add(blocker);
        _banInputBlockLayer = blocker;
        UpdateClickThroughMode();
    }

    public void HideBanInputBlockOverlay()
    {
        if (_banInputBlockLayer != null)
        {
            OverlayCanvas.Children.Remove(_banInputBlockLayer);
            _banInputBlockLayer = null;
        }
        UpdateClickThroughMode();
    }

    public void ShowBanTimedMessageOverlay(string message)
    {
        HideBanTimedMessageOverlay();

        var container = new Grid
        {
            Width = ActualWidth > 0 ? ActualWidth : 1280,
            Height = ActualHeight > 0 ? ActualHeight : 720,
            IsHitTestVisible = true
        };

        var dim = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0x00, 0x00, 0x00)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(48),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.85
            }
        };

        container.Children.Add(dim);
        container.Children.Add(text);
        OverlayCanvas.Children.Add(container);
        _banTimedMessageLayer = container;
        UpdateClickThroughMode();
    }

    public void HideBanTimedMessageOverlay()
    {
        if (_banTimedMessageLayer != null)
        {
            OverlayCanvas.Children.Remove(_banTimedMessageLayer);
            _banTimedMessageLayer = null;
        }
        UpdateClickThroughMode();
    }

    // ?? Ban avatar markers ????????????????????????????????????????????????????

    /// <summary>
    /// Places (or repositions) a "banned" marker badge on each matched operator's
    /// portrait. <paramref name="imgWidth"/> and <paramref name="imgHeight"/> are
    /// the pixel dimensions of the screenshot that produced <paramref name="positions"/>,
    /// used to normalise hit-box coordinates to overlay space.
    /// </summary>
    public void UpdateBanMarkers(
        IReadOnlyList<System.Drawing.Rectangle> positions,
        int imgWidth, int imgHeight)
    {
        if (imgWidth <= 0 || imgHeight <= 0)
            return;

        // Markers are accumulative: once placed they stay until ClearBanMarkers().
        // Never remove a marker mid-session ? TemplateMatch can miss a slot on individual
        // frames, and removing+re-adding causes visible flicker.
        foreach (var hitBox in positions)
        {
            double mx = (double)hitBox.X / imgWidth * ActualWidth;
            double my = (double)hitBox.Y / imgHeight * ActualHeight;

            var marker = CreateBanMarker();
            marker.Opacity = 0;
            Canvas.SetLeft(marker, mx);
            Canvas.SetTop(marker, my);
            OverlayCanvas.Children.Add(marker);
            _banMarkerPool.Add(marker);

            marker.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        }
    }

    /// <summary>
    /// Removes all ban avatar markers with a fade-out animation.
    /// Called when the player leaves the ban screen.
    /// </summary>
    public void ClearBanMarkers()
    {
        foreach (var el in _banMarkerPool.ToList())
        {
            _banMarkerPool.Remove(el);
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            anim.Completed += (_, _) => OverlayCanvas.Children.Remove(el);
            el.BeginAnimation(OpacityProperty, anim);
        }
    }

    private static Grid CreateBanMarker()
    {
        var grid = new Grid
        {
            Width = BanMarkerSize,
            Height = BanMarkerSize
        };

        var ellipse = new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                ShadowDepth = 2,
                Opacity = 0.5
            }
        };

        var label = new TextBlock
        {
            Text = "\u7981",   // ?
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(ellipse);
        grid.Children.Add(label);
        return grid;
    }

    // ?? Helpers ???????????????????????????????????????????????????????????????

    /// <summary>
    /// Toggles the <c>WS_EX_TRANSPARENT</c> extended style on the overlay HWND.
    /// When <paramref name="clickThrough"/> is <c>true</c> (default state) the
    /// overlay is invisible to hit-testing and all input falls through to the game.
    /// When <c>false</c> the overlay can receive mouse events (used briefly while
    /// the instruction layer waits for the player's scroll).
    /// </summary>
    private void SetClickThrough(bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        if (clickThrough)
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
                exStyle | User32.WS_EX_TRANSPARENT);
        else
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
                exStyle & ~User32.WS_EX_TRANSPARENT);
    }

    private void UpdateClickThroughMode()
    {
        bool shouldBlockInput = _banInstructionLayer != null
            || _banInputBlockLayer != null
            || _banTimedMessageLayer != null;
        SetClickThrough(!shouldBlockInput);
    }
}
