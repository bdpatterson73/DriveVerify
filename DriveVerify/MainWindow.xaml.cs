using System.Collections.Specialized;
using System.Windows;
using DriveVerify.ViewModels;

namespace DriveVerify;

public partial class MainWindow : Window
{
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
}