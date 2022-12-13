using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Navigation;

namespace TwitterNotifier
{
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
			var vm = new TwitterNotifierViewModel();
			if (System.Windows.Application.Current is App app)
			{
				app.LoadTheme(vm);
				vm.Settings.PropertyChanged += (s, e) =>
				{
					if (e.PropertyName == "Theme")
					{
						app.LoadTheme(vm);
					}
				};
			}
			DataContext = vm;
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

		private void OnGenerateKeyClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				vm.GenerateKey();
			}
		}

		private void OnLogoutClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				ThreadPool.QueueUserWorkItem(async q =>
				{
					await vm.Logout();
				});
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
				ThreadPool.QueueUserWorkItem(async q =>
				{
					await vm.ValidateAppKey();
				});
			}
		}

		private void OnBrowseOutputDirectoryClick(object sender, RoutedEventArgs e)
		{
			SettingsPopup.StaysOpen = true;
			try
			{
				var dialog = new FolderBrowserDialog
				{
					Description = "Select a directory to store new tweets within",
					SelectedPath = OutputDirectoryTextBox.Text
				};
				if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
				{
					var directory = dialog.SelectedPath;
					if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
					{
						OutputDirectoryTextBox.Text = directory;
					}
				}
			}
			finally
			{
				SettingsPopup.StaysOpen = false;
			}
		}
	}
}
