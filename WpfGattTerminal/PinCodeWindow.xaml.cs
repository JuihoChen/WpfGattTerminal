using System;
using System.Windows;
using System.Windows.Input;

namespace WpfGattTerminal
{
    /// <summary>
    /// Interaction logic for PinCodeWindow.xaml
    /// </summary>
    public partial class PinCodeWindow : Window
    {
        private MainWindow rootWindow;
        private String resultMsg = String.Empty;

        public PinCodeWindow()
        {
            InitializeComponent();

            //Set DataContext for Data Binding
            rootWindow = App.Current.MainWindow as MainWindow;
            DataContext = rootWindow;

            pinTextBox.Focus();
        }

        public String getResultMsg()
        {
            return resultMsg;
        }

        private void pinTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length >= 1)
            {
                if (!char.IsDigit(e.Text, e.Text.Length - 1))
                    e.Handled = true;
            }
        }

        private void pinTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (pinTextBox.Text.Length > 0 && e.Key == Key.Enter)
            {
                resultMsg = pinTextBox.Text;
                this.Close();
            }
        }

    }
}
