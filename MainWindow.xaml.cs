using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace TwitterNotifier
{
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
			DataContext = new TwitterNotifierViewModel();
		}

		private void OnAddKeywordClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				var keyword = KeywordTextBox.Text;
				if (!string.IsNullOrWhiteSpace(keyword))
				{
					vm.Settings.Keywords.Add(keyword);
				}
				KeywordTextBox.Text = string.Empty;
				KeywordTextBox.Focus();
			}
		}

		private void OnRemoveKeywordClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				var keyword = vm.Settings.KeywordsView.CurrentItem;
				if (keyword != null && keyword is string)
				{
					if (vm.Settings.Keywords.Remove((string)keyword))
					{
						vm.Settings.KeywordsView.MoveCurrentToFirst();
					}
				}
			}
		}

		private void OnLoginClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				vm.Authenticate();
			}
		}

		private void OnLogoutClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				vm.Logout();
				SettingsButton.IsChecked = false;
			}
		}

		private void OnLoginNavigate(object sender, RequestNavigateEventArgs e)
		{
			CaptchaTextBox.Focus();
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
			e.Handled = true;
		}

		private void OnValidateAppKeyClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				vm.ValidateAppKey();
			}
		}
	}
}
