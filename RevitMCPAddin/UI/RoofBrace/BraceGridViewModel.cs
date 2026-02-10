// File: UI/RoofBrace/BraceGridViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace RevitMCPAddin.UI.RoofBrace
{
    public class AxisGridLabel
    {
        /// <summary>Grid name to display (e.g. "X1", "EX13").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Normalized position within the bay extent (0..1). Values outside range are allowed and will be clamped by UI.
        /// </summary>
        public double Position01 { get; set; } = 0.0;
    }

    /// <summary>
    /// Overlay line (normalized coordinates 0..1 within bay extent).
    /// Used to show highlighted structural framing members in the grid UI.
    /// </summary>
    public class BraceOverlayLine
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Thickness { get; set; } = 3.0;
        /// <summary>
        /// Optional stroke color in hex (e.g. "#FF8C00"). If empty, UI uses default.
        /// </summary>
        public string StrokeHex { get; set; } = string.Empty;
    }

    public enum BayPattern
    {
        None,
        Slash,
        BackSlash,
        X
    }

    public class BayViewModel : INotifyPropertyChanged
    {
        private BayPattern _pattern;
        private BayPattern _existingPattern;
        private bool _overrideExisting;
        private BraceTypeItem _braceType;

        public int Row { get; }
        public int Column { get; }

        public BayPattern Pattern
        {
            get => _pattern;
            set
            {
                if (_pattern != value)
                {
                    _pattern = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BraceTypeLabel));
                    OnPropertyChanged(nameof(ShowExistingSlash));
                    OnPropertyChanged(nameof(ShowExistingBackSlash));
                }
            }
        }

        /// <summary>
        /// Existing brace pattern detected from the model (if any).
        /// </summary>
        public BayPattern ExistingPattern
        {
            get => _existingPattern;
            set
            {
                if (_existingPattern != value)
                {
                    _existingPattern = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasExisting));
                    OnPropertyChanged(nameof(ShowExistingSlash));
                    OnPropertyChanged(nameof(ShowExistingBackSlash));
                }
            }
        }

        /// <summary>
        /// When true, this bay will override existing braces in the model.
        /// </summary>
        public bool OverrideExisting
        {
            get => _overrideExisting;
            set
            {
                if (_overrideExisting != value)
                {
                    _overrideExisting = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowExistingSlash));
                    OnPropertyChanged(nameof(ShowExistingBackSlash));
                }
            }
        }

        public bool HasExisting => ExistingPattern != BayPattern.None;

        /// <summary>
        /// True when existing brace (\ or X) should be shown in red.
        /// </summary>
        public bool ShowExistingBackSlash =>
            !OverrideExisting &&
            (ExistingPattern == BayPattern.BackSlash || ExistingPattern == BayPattern.X);

        /// <summary>
        /// True when existing brace (/ or X) should be shown in red.
        /// </summary>
        public bool ShowExistingSlash =>
            !OverrideExisting &&
            (ExistingPattern == BayPattern.Slash || ExistingPattern == BayPattern.X);

        public BraceTypeItem BraceType
        {
            get => _braceType;
            set
            {
                if (!ReferenceEquals(_braceType, value))
                {
                    _braceType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BraceTypeLabel));
                }
            }
        }

        /// <summary>
        /// Internal brace type code (maps to MCP braceTypes[*].code).
        /// </summary>
        public string BraceTypeCode => BraceType?.Code ?? string.Empty;

        public string BraceTypeLabel =>
            Pattern == BayPattern.None || BraceType == null
                ? string.Empty
                : BraceType.ShortLabel;

        public BayViewModel(int row, int column)
        {
            Row = row;
            Column = column;
            _pattern = BayPattern.None;
            _existingPattern = BayPattern.None;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// UI-side representation of a brace type.
    /// </summary>
    public class BraceTypeItem
    {
        public string Code { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;

        /// <summary>
        /// Label shown in the combo box: "符号:タイプ名".
        /// </summary>
        public string ShortLabel
        {
            get
            {
                var hasSymbol = !string.IsNullOrEmpty(Symbol);
                var hasType = !string.IsNullOrEmpty(TypeName);
                if (hasSymbol || hasType)
                {
                    var sym = Symbol ?? string.Empty;
                    var type = TypeName ?? string.Empty;
                    if (string.IsNullOrEmpty(sym)) return type;
                    if (string.IsNullOrEmpty(type)) return sym;
                    return $"{sym}:{type}";
                }
                return Code;
            }
        }
    }

    public class BraceGridViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<BayViewModel> Bays { get; } = new ObservableCollection<BayViewModel>();
        public ObservableCollection<BraceTypeItem> BraceTypes { get; } = new ObservableCollection<BraceTypeItem>();
        public ObservableCollection<string> XGridNames { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> YGridNames { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Optional column width weights (length == ColumnCount). If null/invalid, UI uses uniform widths.
        /// </summary>
        public IList<double> ColumnWeights { get; private set; }

        /// <summary>
        /// Optional row height weights (length == RowCount). If null/invalid, UI uses uniform heights.
        /// </summary>
        public IList<double> RowWeights { get; private set; }

        // Axis labels (actual Revit Grid labels) are independent from bay boundaries.
        public ObservableCollection<AxisGridLabel> XGridLabels { get; } = new ObservableCollection<AxisGridLabel>();
        public ObservableCollection<AxisGridLabel> YGridLabels { get; } = new ObservableCollection<AxisGridLabel>();

        // Overlay lines for highlighted structural framing members.
        public ObservableCollection<BraceOverlayLine> HighlightLines { get; } = new ObservableCollection<BraceOverlayLine>();

        private BraceTypeItem _selectedGlobalBraceType;
        public BraceTypeItem SelectedGlobalBraceType
        {
            get => _selectedGlobalBraceType;
            set
            {
                if (!ReferenceEquals(_selectedGlobalBraceType, value))
                {
                    _selectedGlobalBraceType = value;
                    OnPropertyChanged();
                }
            }
        }

        public int RowCount { get; }
        public int ColumnCount { get; }
        public int ColumnGridCount => ColumnCount + 1;
        public int RowGridCount => RowCount + 1;

        private bool? _dialogResult;
        public bool? DialogResult
        {
            get => _dialogResult;
            set { _dialogResult = value; OnPropertyChanged(); }
        }

        private string _promptText;
        public string PromptText
        {
            get => _promptText;
            set { _promptText = value; OnPropertyChanged(); }
        }

        private string _promptSummary;
        public string PromptSummary
        {
            get => _promptSummary;
            set { _promptSummary = value; OnPropertyChanged(); }
        }

        public ICommand TogglePatternCommand { get; }

        /// <summary>
        /// rows: number of bay rows (Y direction, bottom to top)
        /// columns: number of bay columns (X direction, left to right)
        /// xGridNames / yGridNames: names for grid lines (count should be columns+1 / rows+1; will be clamped if not).
        /// existingPatterns: map of (row,col) to existing bay pattern in the model.
        /// </summary>
        public BraceGridViewModel(
            int rows,
            int columns,
            IList<string> xGridNames,
            IList<string> yGridNames,
            IDictionary<(int row, int col), BayPattern> existingPatterns = null)
        {
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));

            RowCount = rows;
            ColumnCount = columns;

            // Initialize bays (row-major)
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    var bay = new BayViewModel(r, c);
                    if (existingPatterns != null &&
                        existingPatterns.TryGetValue((r, c), out var pattern) &&
                        pattern != BayPattern.None)
                    {
                        bay.ExistingPattern = pattern;
                    }
                    Bays.Add(bay);
                }
            }

            // Grid line names
            int xCount = ColumnGridCount;
            int yCount = RowGridCount;

            for (int i = 0; i < xCount; i++)
            {
                string name = (xGridNames != null && i < xGridNames.Count && !string.IsNullOrWhiteSpace(xGridNames[i]))
                    ? xGridNames[i]
                    : $"X{i + 1}";
                XGridNames.Add(name);
            }

            for (int j = 0; j < yCount; j++)
            {
                string name = (yGridNames != null && j < yGridNames.Count && !string.IsNullOrWhiteSpace(yGridNames[j]))
                    ? yGridNames[j]
                    : $"Y{j + 1}";
                YGridNames.Add(name);
            }

            TogglePatternCommand = new RelayCommand(param =>
            {
                var bay = param as BayViewModel;
                if (bay == null)
                    return;

                var currentType = SelectedGlobalBraceType;

                // If already has a pattern and global type changed, keep pattern and only update type.
                if (bay.Pattern != BayPattern.None &&
                    currentType != null &&
                    !ReferenceEquals(bay.BraceType, currentType))
                {
                    bay.BraceType = currentType;
                    return;
                }

                // First click on a bay with existing braces but no override:
                // switch to override mode so black pattern lines are used.
                if (bay.Pattern == BayPattern.None && bay.HasExisting && !bay.OverrideExisting)
                {
                    bay.OverrideExisting = true;
                }

                if (bay.Pattern == BayPattern.None)
                {
                    // First non-empty pattern remembers current global type.
                    if (currentType != null && bay.BraceType == null)
                    {
                        bay.BraceType = currentType;
                    }
                }

                bay.Pattern = bay.Pattern switch
                {
                    BayPattern.None => BayPattern.X,
                    BayPattern.X => BayPattern.BackSlash,
                    BayPattern.BackSlash => BayPattern.Slash,
                    BayPattern.Slash => BayPattern.None,
                    _ => BayPattern.None
                };

                // When user cycles back to None, revert to "no override" mode
                // so existing red lines (if any) are shown and model is untouched.
                if (bay.Pattern == BayPattern.None)
                {
                    bay.OverrideExisting = false;
                    bay.BraceType = null;
                }
            });
        }

        /// <summary>
        /// Set brace type list from MCP braceTypes definitions.
        /// </summary>
        public void SetBraceTypes(IEnumerable<BraceTypeItem> items, string defaultCode)
        {
            BraceTypes.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null) continue;
                    BraceTypes.Add(item);
                }
            }

            if (BraceTypes.Count == 0)
            {
                SelectedGlobalBraceType = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(defaultCode))
            {
                var match = BraceTypes.FirstOrDefault(t =>
                    string.Equals(t.Code, defaultCode, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    SelectedGlobalBraceType = match;
                    return;
                }
            }

            SelectedGlobalBraceType = BraceTypes[0];
        }

        public void SetAxisGridLabels(IEnumerable<AxisGridLabel> xLabels, IEnumerable<AxisGridLabel> yLabels)
        {
            XGridLabels.Clear();
            if (xLabels != null)
            {
                foreach (var x in xLabels)
                {
                    if (x == null) continue;
                    if (string.IsNullOrWhiteSpace(x.Name)) continue;
                    XGridLabels.Add(x);
                }
            }

            YGridLabels.Clear();
            if (yLabels != null)
            {
                foreach (var y in yLabels)
                {
                    if (y == null) continue;
                    if (string.IsNullOrWhiteSpace(y.Name)) continue;
                    YGridLabels.Add(y);
                }
            }
        }

        public void SetHighlightLines(IEnumerable<BraceOverlayLine> lines)
        {
            HighlightLines.Clear();
            if (lines == null) return;
            foreach (var ln in lines)
            {
                if (ln == null) continue;
                HighlightLines.Add(ln);
            }
        }

        /// <summary>
        /// Set column/row weights for proportional grid scaling.
        /// </summary>
        public void SetGridWeights(IList<double> columnWeights, IList<double> rowWeights)
        {
            ColumnWeights = columnWeights;
            RowWeights = rowWeights;
            OnPropertyChanged(nameof(ColumnWeights));
            OnPropertyChanged(nameof(RowWeights));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
