// File: UI/RoofBrace/BraceGridWindow.xaml.cs
using System;
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
                XHeaderCanvas == null || YHeaderCanvas == null)
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

            double cellWidth = width / cols;
            double cellHeight = height / rows;

            // Resize canvas to match bay area
            GridLinesCanvas.Width = width;
            GridLinesCanvas.Height = height;
            XHeaderCanvas.Width = width;
            YHeaderCanvas.Height = height;

            // Clear previous visuals
            GridLinesCanvas.Children.Clear();
            XHeaderCanvas.Children.Clear();
            YHeaderCanvas.Children.Clear();

            var lineBrush = Brushes.Gray;

            // Vertical lines (X grid)
            for (int i = 0; i <= cols; i++)
            {
                double x = i * cellWidth;
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

                    double x = i * cellWidth;
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
                double y = j * cellHeight;
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

                    double y = j * cellHeight;
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
    }
}
