// File: UI/RoofBrace/BraceGridWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RevitMCPAddin.UI.RoofBrace
{
    public partial class BraceGridWindow : Window
    {
        public BraceGridViewModel ViewModel { get; }

        public BraceGridWindow(BraceGridViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = ViewModel;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DialogResult = false;
            Close();
        }

        private void BayItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridVisuals();
        }

        private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // X headers follow horizontal scroll only
            if (XHeaderScrollViewer != null && !double.IsNaN(e.HorizontalOffset))
            {
                XHeaderScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
            }

            // Y headers follow vertical scroll only
            if (YHeaderScrollViewer != null && !double.IsNaN(e.VerticalOffset))
            {
                YHeaderScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            }

            // Keep grid lines canvas in sync with content size (in case of dynamic layout)
            UpdateGridVisuals();
        }

        private void UpdateGridVisuals()
        {
            if (BayItemsControl == null || GridLinesCanvas == null ||
                XHeaderCanvas == null || YHeaderCanvas == null || HighlightLinesCanvas == null)
            {
                return;
            }

            var vm = DataContext as BraceGridViewModel;
            if (vm == null)
            {
                return;
            }

            int cols = vm.ColumnCount;
            int rows = vm.RowCount;
            if (cols <= 0 || rows <= 0)
            {
                return;
            }

            double width = BayItemsControl.ActualWidth;
            double height = BayItemsControl.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var colWeights = GetWeights(vm.ColumnWeights, cols);
            var rowWeights = GetWeights(vm.RowWeights, rows);

            // Ensure items panel grid matches weights (proportional columns/rows)
            ApplyGridDefinitions(colWeights, rowWeights);

            var xBoundaries = BuildBoundaries(colWeights, width);
            var yBoundaries = BuildBoundaries(rowWeights, height);

            // Resize canvas to match bay area
            GridLinesCanvas.Width = width;
            GridLinesCanvas.Height = height;
            XHeaderCanvas.Width = width;
            YHeaderCanvas.Height = height;
            HighlightLinesCanvas.Width = width;
            HighlightLinesCanvas.Height = height;

            // Clear previous visuals
            GridLinesCanvas.Children.Clear();
            XHeaderCanvas.Children.Clear();
            YHeaderCanvas.Children.Clear();
            HighlightLinesCanvas.Children.Clear();

            var lineBrush = Brushes.Gray;

            // Vertical lines (X grid)
            for (int i = 0; i <= cols; i++)
            {
                double x = xBoundaries[i];
                var vLine = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = height,
                    Stroke = lineBrush,
                    StrokeThickness = 1
                };
                GridLinesCanvas.Children.Add(vLine);
            }

            // X axis labels (independent from bay boundaries)
            if (vm.XGridLabels != null && vm.XGridLabels.Count > 0)
            {
                foreach (var lbl in vm.XGridLabels)
                {
                    if (lbl == null) continue;
                    if (string.IsNullOrWhiteSpace(lbl.Name)) continue;

                    double x = lbl.Position01 * width;

                    var tb = new TextBlock
                    {
                        Text = lbl.Name,
                        FontWeight = FontWeights.Bold
                    };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                    double tx = x - tb.DesiredSize.Width / 2.0;
                    double maxX = Math.Max(0, XHeaderCanvas.Width - tb.DesiredSize.Width);
                    if (tx < 0) tx = 0;
                    else if (tx > maxX) tx = maxX;

                    double ty = Math.Max(0, XHeaderCanvas.Height - tb.DesiredSize.Height);

                    Canvas.SetLeft(tb, tx);
                    Canvas.SetTop(tb, ty);
                    XHeaderCanvas.Children.Add(tb);
                }
            }
            else
            {
                // Fallback: one label per bay boundary
                for (int i = 0; i <= cols; i++)
                {
                    if (i >= vm.XGridNames.Count) continue;

                    double x = xBoundaries[i];
                    string name = vm.XGridNames[i];
                    var tb = new TextBlock
                    {
                        Text = name,
                        FontWeight = FontWeights.Bold
                    };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                    double tx = x - tb.DesiredSize.Width / 2.0;
                    double maxX = Math.Max(0, XHeaderCanvas.Width - tb.DesiredSize.Width);
                    if (tx < 0) tx = 0;
                    else if (tx > maxX) tx = maxX;

                    double ty = Math.Max(0, XHeaderCanvas.Height - tb.DesiredSize.Height);

                    Canvas.SetLeft(tb, tx);
                    Canvas.SetTop(tb, ty);
                    XHeaderCanvas.Children.Add(tb);
                }
            }

            // Horizontal lines (Y grid)
            for (int j = 0; j <= rows; j++)
            {
                double y = yBoundaries[j];
                var hLine = new Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = lineBrush,
                    StrokeThickness = 1
                };
                GridLinesCanvas.Children.Add(hLine);
            }

            // Highlighted structural framing members (thick lines)
            if (vm.HighlightLines != null && vm.HighlightLines.Count > 0)
            {
                foreach (var ln in vm.HighlightLines)
                {
                    if (ln == null) continue;
                    double x1 = ln.X1 * width;
                    double x2 = ln.X2 * width;
                    // Y is inverted (0 = bottom in model -> top in canvas)
                    double y1 = (1.0 - ln.Y1) * height;
                    double y2 = (1.0 - ln.Y2) * height;

                    var stroke = ResolveStroke(ln.StrokeHex) ?? Brushes.DarkOrange;

                    var hLine = new Line
                    {
                        X1 = x1,
                        X2 = x2,
                        Y1 = y1,
                        Y2 = y2,
                        Stroke = stroke,
                        StrokeThickness = ln.Thickness <= 0 ? 3.0 : ln.Thickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    HighlightLinesCanvas.Children.Add(hLine);
                }
            }

            // Y axis labels (independent from bay boundaries)
            if (vm.YGridLabels != null && vm.YGridLabels.Count > 0)
            {
                foreach (var lbl in vm.YGridLabels)
                {
                    if (lbl == null) continue;
                    if (string.IsNullOrWhiteSpace(lbl.Name)) continue;

                    // Revit: +Y is up. WPF canvas: +Y is down.
                    // Position01 is normalized as 0=bottom(minY) .. 1=top(maxY), so invert for display.
                    double y = (1.0 - lbl.Position01) * height;

                    var tb = new TextBlock
                    {
                        Text = lbl.Name,
                        FontWeight = FontWeights.Bold
                    };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                    double ty = y - tb.DesiredSize.Height / 2.0;
                    double maxY = Math.Max(0, YHeaderCanvas.Height - tb.DesiredSize.Height);
                    if (ty < 0) ty = 0;
                    else if (ty > maxY) ty = maxY;

                    double tx = Math.Max(0, YHeaderCanvas.Width - tb.DesiredSize.Width);

                    Canvas.SetLeft(tb, tx);
                    Canvas.SetTop(tb, ty);
                    YHeaderCanvas.Children.Add(tb);
                }
            }
            else
            {
                // Fallback: one label per bay boundary
                for (int j = 0; j <= rows; j++)
                {
                    int nameIdx = rows - j; // top-to-bottom display
                    if (nameIdx < 0 || nameIdx >= vm.YGridNames.Count) continue;

                    double y = yBoundaries[j];
                    string name = vm.YGridNames[nameIdx];
                    var tb = new TextBlock
                    {
                        Text = name,
                        FontWeight = FontWeights.Bold
                    };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                    double ty = y - tb.DesiredSize.Height / 2.0;
                    double maxY = Math.Max(0, YHeaderCanvas.Height - tb.DesiredSize.Height);
                    if (ty < 0) ty = 0;
                    else if (ty > maxY) ty = maxY;

                    double tx = Math.Max(0, YHeaderCanvas.Width - tb.DesiredSize.Width);

                    Canvas.SetLeft(tb, tx);
                    Canvas.SetTop(tb, ty);
                    YHeaderCanvas.Children.Add(tb);
                }
            }
        }

        private static IList<double> GetWeights(IList<double> weights, int count)
        {
            if (weights == null || weights.Count != count)
            {
                return Enumerable.Repeat(1.0, count).ToList();
            }

            var sanitized = new List<double>(count);
            foreach (var w in weights)
            {
                sanitized.Add(w > 1e-9 ? w : 1.0);
            }
            return sanitized;
        }

        private void ApplyGridDefinitions(IList<double> colWeights, IList<double> rowWeights)
        {
            var panel = FindItemsPanelGrid();
            if (panel == null) return;

            if (panel.ColumnDefinitions.Count != colWeights.Count)
            {
                panel.ColumnDefinitions.Clear();
                foreach (var w in colWeights)
                {
                    panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
                }
            }
            else
            {
                for (int i = 0; i < colWeights.Count; i++)
                {
                    panel.ColumnDefinitions[i].Width = new GridLength(colWeights[i], GridUnitType.Star);
                }
            }

            if (panel.RowDefinitions.Count != rowWeights.Count)
            {
                panel.RowDefinitions.Clear();
                foreach (var w in rowWeights)
                {
                    panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(w, GridUnitType.Star) });
                }
            }
            else
            {
                for (int i = 0; i < rowWeights.Count; i++)
                {
                    panel.RowDefinitions[i].Height = new GridLength(rowWeights[i], GridUnitType.Star);
                }
            }
        }

        private static double[] BuildBoundaries(IList<double> weights, double total)
        {
            int n = weights.Count;
            var boundaries = new double[n + 1];
            double sum = weights.Sum();
            if (sum <= 1e-9) sum = 1.0;
            double acc = 0.0;
            boundaries[0] = 0.0;
            for (int i = 0; i < n; i++)
            {
                acc += weights[i] / sum;
                boundaries[i + 1] = acc * total;
            }
            // Ensure last boundary aligns
            boundaries[n] = total;
            return boundaries;
        }

        private static Brush ResolveStroke(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return null;
            }
        }

        private Grid FindItemsPanelGrid()
        {
            if (BayItemsControl == null) return null;
            var presenter = FindVisualChild<ItemsPresenter>(BayItemsControl);
            if (presenter == null)
                return null;
            presenter.ApplyTemplate();
            var panel = VisualTreeHelper.GetChild(presenter, 0) as Grid;
            return panel;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;
                var found = FindVisualChild<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
