using System.ComponentModel;

namespace TwitterNotifier
{
	public class TwitterSettings : INotifyPropertyChanged
	{
		#region Properties

		private double _fontSize = 16;
		public double FontSize
		{
			get { return _fontSize; }
			set
			{
				if (_fontSize != value)
				{
					_fontSize = value;
					OnPropertyChanged("FontSize");
				}
			}
		}

		private double _volume = 1;
		public double Volume
		{
			get { return _volume; }
			set
			{
				if (_volume != value)
				{
					_volume = value;
					OnPropertyChanged("Volume");
				}
			}
		}

		#endregion

		#region INotifyPropertyChanged Members

		public event PropertyChangedEventHandler PropertyChanged;
		private void OnPropertyChanged(string propertyName)
		{
			var handler = PropertyChanged;
			if (handler != null)
			{
				handler(this, new PropertyChangedEventArgs(propertyName));
			}
		}

		#endregion
	}
}
