using System.ComponentModel;

namespace Island.Services;

public sealed class TodoItem : INotifyPropertyChanged
{
	private string _text = "";

	private bool _done;

	public string Text
	{
		get
		{
			return _text;
		}
		set
		{
			_text = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Text"));
		}
	}

	public bool Done
	{
		get
		{
			return _done;
		}
		set
		{
			_done = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Done"));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}
