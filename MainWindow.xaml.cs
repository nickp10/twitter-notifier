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

		private void OnLoginClick(object sender, RoutedEventArgs e)
		{
			var vm = DataContext as TwitterNotifierViewModel;
			if (vm != null)
			{
				vm.Authenticate();
			}
		}

		private void OnLoginNavigate(object sender, RequestNavigateEventArgs e)
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
			e.Handled = true;
		}
	}
}
