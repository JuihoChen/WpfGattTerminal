using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfGattTerminal.Themes
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// @class  SearchEventArgs
    ///
    /// @brief  Additional information for search events. 
    ///
    /// @author Le Duc Anh
    /// @date   8/14/2010
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public class SearchEventArgs: RoutedEventArgs{
        private string m_keyword="";

        public string Keyword
        {
            get { return m_keyword; }
            set { m_keyword = value; }
        }

        public SearchEventArgs(): base(){

        }
        public SearchEventArgs(RoutedEvent routedEvent): base(routedEvent){

        }
    }

    public class SearchTextBox : TextBox {

        public static DependencyProperty LabelTextProperty =
            DependencyProperty.Register(
                "LabelText",
                typeof(string),
                typeof(SearchTextBox));

        public static DependencyProperty LabelTextColorProperty =
            DependencyProperty.Register(
                "LabelTextColor",
                typeof(Brush),
                typeof(SearchTextBox));

        private static DependencyPropertyKey HasTextPropertyKey =
            DependencyProperty.RegisterReadOnly(
                "HasText",
                typeof(bool),
                typeof(SearchTextBox),
                new PropertyMetadata());
        public static DependencyProperty HasTextProperty = HasTextPropertyKey.DependencyProperty;

        private static DependencyPropertyKey IsMouseLeftButtonDownPropertyKey =
            DependencyProperty.RegisterReadOnly(
                "IsMouseLeftButtonDown",
                typeof(bool),
                typeof(SearchTextBox),
                new PropertyMetadata());
        public static DependencyProperty IsMouseLeftButtonDownProperty = IsMouseLeftButtonDownPropertyKey.DependencyProperty;

        public static readonly RoutedEvent SearchEvent = 
            EventManager.RegisterRoutedEvent(
                "Search",
                RoutingStrategy.Bubble,
                typeof(RoutedEventHandler),
                typeof(SearchTextBox));

        static SearchTextBox() {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SearchTextBox),
                new FrameworkPropertyMetadata(typeof(SearchTextBox)));
        }

        public SearchTextBox()
            : base() {

        }

        protected override void OnTextChanged(TextChangedEventArgs e) {
            base.OnTextChanged(e);
            
            HasText = Text.Length != 0;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn protected override void OnMouseDown(MouseButtonEventArgs e)
        ///
        /// @brief  Override the default method. 
        ///
        /// @author Le Duc Anh
        /// @date   8/14/2010
        ///
        /// @param  e   Event information to send to registered event handlers. 
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            // if users click on the editing area, the pop up will be hidden
            //Type sourceType=e.OriginalSource.GetType();
            //if (sourceType!= typeof(Image))
            //    HidePopup();
        }

        public override void OnApplyTemplate() {
            base.OnApplyTemplate();

            this.MouseLeave += new MouseEventHandler(SearchTextBox_MouseLeave);
            Border iconBorder = GetTemplateChild("PART_SearchIconBorder") as Border;
            if (iconBorder != null) {
                iconBorder.MouseLeftButtonDown += new MouseButtonEventHandler(IconBorder_MouseLeftButtonDown);
                iconBorder.MouseLeftButtonUp += new MouseButtonEventHandler(IconBorder_MouseLeftButtonUp);
                iconBorder.MouseLeave += new MouseEventHandler(IconBorder_MouseLeave);
            }
        }

        void SearchTextBox_MouseLeave(object sender, MouseEventArgs e)
        {
            //if (!m_listPopup.IsMouseOver)
            //    HidePopup();
        }

        private void IconBorder_MouseLeftButtonDown(object obj, MouseButtonEventArgs e) {
            IsMouseLeftButtonDown = true;

            Border iconBorder = obj as Border;
            //iconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5D16F"));
        }

        private void IconBorder_MouseLeftButtonUp(object obj, MouseButtonEventArgs e) {
            if (!IsMouseLeftButtonDown) return;

            if (HasText ) {
                RaiseSearchEvent();
            }

            IsMouseLeftButtonDown = false;
        }

        private void IconBorder_MouseLeave(object obj, MouseEventArgs e) {
            IsMouseLeftButtonDown = false;
            
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                this.Text = "";
            }
            //else if ((e.Key == Key.Return || e.Key == Key.Enter)) {
            //    RaiseSearchEvent();
            //}
            else {
                base.OnKeyDown(e);
            }
        }

        private void RaiseSearchEvent() {
            if (this.Text == "")
                return;

            //SearchEventArgs args = new SearchEventArgs(SearchEvent);
            //args.Keyword = this.Text;
            //RaiseEvent(args);
            this.Text = "";
        }

        public string LabelText {
            get { return (string)GetValue(LabelTextProperty); }
            set { SetValue(LabelTextProperty, value); }
        }

        public Brush LabelTextColor {
            get { return (Brush)GetValue(LabelTextColorProperty); }
            set { SetValue(LabelTextColorProperty, value); }
        }

        public bool HasText {
            get { return (bool)GetValue(HasTextProperty); }
            private set { SetValue(HasTextPropertyKey, value); }
        }

        public bool IsMouseLeftButtonDown {
            get { return (bool)GetValue(IsMouseLeftButtonDownProperty); }
            private set { SetValue(IsMouseLeftButtonDownPropertyKey, value); }
        }

        public event RoutedEventHandler OnSearch {
            add { AddHandler(SearchEvent, value); }
            remove { RemoveHandler(SearchEvent, value); }
        }

#region Stuff added by Le Duc Anh

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn private void ShowPopup(UIElement item)
        ///
        /// @brief  Shows the pop up. 
        ///
        /// @author Le Duc Anh
        /// @date   8/14/2010
        ///
        /// @param  item    The item. 
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            if (!HasText)
                this.LabelText = "Search";
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);
            if (!HasText)
                this.LabelText = "";
        }

#endregion
    }
}
