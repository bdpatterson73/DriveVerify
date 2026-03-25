using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DriveVerify.ViewModels;

namespace DriveVerify;

public partial class MainWindow : Window
{
    private bool _isHeatmapExpanded;
    private GridLength[] _savedRowHeights = [];
    private double _savedHeatmapHeight;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Auto-scroll log to bottom
        if (LogListBox.Items is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += (_, _) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            };
        }
    }

    private void HeatmapBorder_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;

        e.Handled = true;
        var rows = MainGrid.RowDefinitions;

        if (!_isHeatmapExpanded)
        {
            // Save current row heights
            _savedRowHeights = new GridLength[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                _savedRowHeights[i] = rows[i].Height;

            _savedHeatmapHeight = HeatmapView.Height;

            // Collapse every row except the heatmap (row 2)
            for (int i = 0; i < rows.Count; i++)
            {
                if (i == 2)
                    rows[i].Height = new GridLength(1, GridUnitType.Star);
                else
                    rows[i].Height = new GridLength(0);
            }

            // Remove the fixed height and let it fill the Grid row
            HeatmapView.Height = double.NaN;
            HeatmapHintText.Text = "Double-click to collapse";
        }
        else
        {
            // Restore saved row heights
            for (int i = 0; i < rows.Count && i < _savedRowHeights.Length; i++)
                rows[i].Height = _savedRowHeights[i];

            HeatmapView.Height = _savedHeatmapHeight;
            HeatmapHintText.Text = "Double-click to expand";
        }

        _isHeatmapExpanded = !_isHeatmapExpanded;
    }
}