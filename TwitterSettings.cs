using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Newtonsoft.Json;

namespace TwitterNotifier
{
	public class TwitterSettings : INotifyPropertyChanged
	{
		#region Properties

		private string _authKey;
		public string AuthKey
		{
			get { return _authKey; }
			set
			{
				if (_authKey != value)
				{
					_authKey = value;
					OnPropertyChanged("AuthKey");
				}
			}
		}

		private string _authSecret;
		public string AuthSecret
		{
			get { return _authSecret; }
			set
			{
				if (_authSecret != value)
				{
					_authSecret = value;
					OnPropertyChanged("AuthSecret");
				}
			}
		}

		private double _fontSize = 18;
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

		private bool _ignoreRetweets = true;
		public bool IgnoreRetweets
		{
			get { return _ignoreRetweets; }
			set
			{
				if (_ignoreRetweets != value)
				{
					_ignoreRetweets = value;
					OnPropertyChanged("IgnoreRetweets");
				}
			}
		}

		private bool _ignoreReplyTos = true;
		public bool IgnoreReplyTos
		{
			get { return _ignoreReplyTos; }
			set
			{
				if (_ignoreReplyTos != value)
				{
					_ignoreReplyTos = value;
					OnPropertyChanged("IgnoreReplyTos");
				}
			}
		}

		private readonly ObservableCollection<string> _keywords = new ObservableCollection<string>();
		public ObservableCollection<string> Keywords
		{
			get { return _keywords; }
		}

		private ListCollectionView _keywordsView;
		[JsonIgnore]
		public ListCollectionView KeywordsView
		{
			get
			{
				if (_keywordsView == null)
				{
					Application.Current.Dispatcher.Invoke(new Action(() =>
					{
						_keywordsView = new ListCollectionView(Keywords);
					}));
				}
				return _keywordsView;
			}
		}

		private bool _rememberMe = true;
		public bool RememberMe
		{
			get { return _rememberMe; }
			set
			{
				if (_rememberMe != value)
				{
					_rememberMe = value;
					OnPropertyChanged("RememberMe");
				}
			}
		}

		private double _urgentVolume = 1;
		public double UrgentVolume
		{
			get { return _urgentVolume; }
			set
			{
				if (_urgentVolume != value)
				{
					_urgentVolume = value;
					OnPropertyChanged("UrgentVolume");
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
