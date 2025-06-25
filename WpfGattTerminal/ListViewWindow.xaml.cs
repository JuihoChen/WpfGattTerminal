using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfGattTerminal
{
    /// <summary>
    /// Interaction logic for ListViewWindow.xaml
    /// </summary>
    public partial class ListViewWindow : Window
    {
        private MainWindow rootWindow;
        private String resultMsg = String.Empty;

        public ListViewWindow()
        {
            InitializeComponent();

            //Set DataContext for Data Binding
            rootWindow = App.Current.MainWindow as MainWindow;
            DataContext = rootWindow;

            rootWindow.NameFilter = rootWindow.NameFilter.Trim();
        }

        public String getResultMsg()
        {
            return resultMsg;
        }

        private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeviceInformationDisplay d = resultsListView.SelectedItem as DeviceInformationDisplay;
            if (d != null)
            {
                resultMsg = "UNNAMED";
                if (d.Name.Trim().Length != 0)
                {
                    resultMsg = d.Id.Substring(d.Id.Length - MainWindow.MAC_LENGTH);
                }
            }
            this.Close();
        }

        // WPF : Scrollview Does Not Work With ListView Inside
        // http://arsnotes.blogspot.tw/2015/03/wpf-listview-swallow-scroll-and-touch.html
        private void resultsListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
                eventArg.RoutedEvent = UIElement.MouseWheelEvent;
                eventArg.Source = sender;
                var obj = ((Control)myScrollViewer) as UIElement;
                obj.RaiseEvent(eventArg);
            }
        }

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length >= 1)
            {
                char c = e.Text[e.Text.Length - 1];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    e.Handled = true;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            rootWindow.NameFilter = filterTextBox.Text;
            resultsListView.Items.Refresh();
        }

#if false
        static private bool Once = true;
        private void resultsListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (Once)
            {
                Once = false;
                resultsListView.Items.Refresh();
            }
        }
#endif
    }
}
