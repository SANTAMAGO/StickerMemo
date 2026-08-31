using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StickerMemo.Models;
using StickerMemo.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace StickerMemo.Views
{
    public partial class AppConfirmDialog : Window
    {
        public bool Result { get; private set; } = false;

        public AppConfirmDialog()
        {
            InitializeComponent();
            AppIconHelper.ApplyAppIcon(this);
            KeyDown += OnWindowKeyDown;
        }

        public static bool Show(
            Window? owner,
            string title = "Delete this note?",
            string message = "This action cannot be undone.",
            string confirmText = "Delete",
            string cancelText = "Cancel",
            bool isDestructive = true,
            string? themeId = null)
        {
            var dialog = new AppConfirmDialog();

            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.DialogTitleText.Text = title;
            dialog.DialogMessageText.Text = message;
            dialog.ConfirmBtn.Content = confirmText;
            dialog.CancelBtn.Content = cancelText;

            if (isDestructive)
            {
                dialog.ConfirmBtn.Style = (Style)dialog.FindResource("DialogDestructiveButtonStyle");
            }
            else
            {
                dialog.ConfirmBtn.Style = (Style)dialog.FindResource("DialogPrimaryButtonStyle");
            }

            if (!string.IsNullOrEmpty(themeId))
            {
                var theme = ThemeColors.GetTheme(themeId);
                dialog.DialogCardBorder.Background = theme.BackgroundBrush;
                dialog.DialogCardBorder.BorderBrush = theme.BorderBrush;
            }

            dialog.ShowDialog();
            return dialog.Result;
        }

        public static void ShowAlert(
            Window? owner,
            string title,
            string message,
            string confirmText = "OK",
            string? themeId = null)
        {
            var dialog = new AppConfirmDialog();

            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.DialogTitleText.Text = title;
            dialog.DialogMessageText.Text = message;
            dialog.ConfirmBtn.Content = confirmText;
            dialog.ConfirmBtn.Style = (Style)dialog.FindResource("DialogPrimaryButtonStyle");
            dialog.CancelBtn.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrEmpty(themeId))
            {
                var theme = ThemeColors.GetTheme(themeId);
                dialog.DialogCardBorder.Background = theme.BackgroundBrush;
                dialog.DialogCardBorder.BorderBrush = theme.BorderBrush;
            }

            dialog.ShowDialog();
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
            Close();
        }

        private void OnGridMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Result = false;
                DialogResult = false;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Result = true;
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }
    }
}
