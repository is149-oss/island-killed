using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace Island.Services;

public static class BrowserTabs
{
	public sealed record Found(string? TabTitle, string? Title, string? Artist, bool HasControls, bool? Playing);

	private static AutomationElement? _prev;

	private static AutomationElement? _playPause;

	private static AutomationElement? _next;

	private static readonly object Sync = new object();

	private static readonly Condition TabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);

	private static readonly Condition ListItemCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);

	private static readonly Condition ButtonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

	public static Found? Find(string process, bool quiet = false)
	{
		if (ReviewSafety.Enabled) return null;
		try
		{
			AutomationElement[] array = Windows(process);
			AutomationElement[] array2 = array;
			foreach (AutomationElement window in array2)
			{
				AutomationElement? automationElement = (quiet ? null : PlayingTab(window));
				if (automationElement == null)
				{
					continue;
				}
				string name = automationElement.Current.Name;
				if (IsSelected(automationElement))
				{
					Found? found = ReadVkPlayer(window, name);
					if ((object)found != null)
					{
						return found;
					}
				}
				Forget();
				return new Found(name, null, null, HasControls: false, null);
			}
			array2 = array;
			foreach (AutomationElement automationElement2 in array2)
			{
				AutomationElement? automationElement3 = automationElement2.FindAll(TreeScope.Descendants, TabCondition).Cast<AutomationElement>().FirstOrDefault(IsSelected);
				if (automationElement3 != null)
				{
					Found? found2 = ReadVkPlayer(automationElement2, automationElement3.Current.Name);
					if ((object)found2 != null)
					{
						return found2;
					}
				}
			}
		}
		catch (Exception)
		{
		}
		Forget();
		return null;
	}

	public static bool Press(string which)
	{
		if (ReviewSafety.Enabled) return false;
		AutomationElement automationElement2;
		lock (Sync)
		{
			AutomationElement automationElement;
			if (which == "prev")
			{
				automationElement = _prev;
			}
			else
			{
				automationElement = ((!(which == "next")) ? _playPause : _next);
			}
			automationElement2 = automationElement;
		}
		if (automationElement2 == null)
		{
			return false;
		}
		try
		{
			((InvokePattern)automationElement2.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
			return true;
		}
		catch (Exception)
		{
			Forget();
			return false;
		}
	}

	private static void Forget()
	{
		lock (Sync)
		{
			_prev = (_playPause = (_next = null));
		}
	}

	private static AutomationElement[] Windows(string process)
	{
		HashSet<int> ids = Process.GetProcessesByName(process).Select((Process p) =>
		{
			using (p)
			{
				return p.Id;
			}
		}).ToHashSet();
		return (from AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
			where ids.Contains(w.Current.ProcessId) && w.Current.ClassName.StartsWith("Chrome_", StringComparison.Ordinal)
			select w).ToArray();
	}

	private static bool IsSelected(AutomationElement tab)
	{
		if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var patternObject))
		{
			return ((SelectionItemPattern)patternObject).Current.IsSelected;
		}
		return false;
	}

	private static AutomationElement? PlayingTab(AutomationElement window)
	{
		foreach (AutomationElement item in window.FindAll(TreeScope.Descendants, TabCondition))
		{
			foreach (AutomationElement item2 in item.FindAll(TreeScope.Children, Condition.TrueCondition))
			{
				string className = item2.Current.ClassName;
				if (className.Contains("MediaIndicator", StringComparison.Ordinal) || className.Contains("AlertIndicator", StringComparison.Ordinal))
				{
					return item;
				}
			}
		}
		return null;
	}

	private static Found? ReadVkPlayer(AutomationElement window, string tabTitle)
	{
		AutomationElement automationElement = window.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document), new PropertyCondition(AutomationElement.NameProperty, tabTitle)));
		if (automationElement == null)
		{
			return null;
		}
		AutomationElement? automationElement2 = automationElement.FindAll(TreeScope.Descendants, ListItemCondition).Cast<AutomationElement>().FirstOrDefault((AutomationElement e) => e.Current.ClassName.Contains("TopNavigation__player", StringComparison.Ordinal));
		if (automationElement2 == null)
		{
			return null;
		}
		AutomationElement[] array = automationElement2.FindAll(TreeScope.Descendants, ButtonCondition).Cast<AutomationElement>().ToArray();
		string text = automationElement2.Current.Name;
		foreach (AutomationElement item in array.Reverse())
		{
			string name = item.Current.Name;
			if (name.Length > 0 && text.EndsWith(name, StringComparison.Ordinal))
			{
				string text2 = text;
				int length = name.Length;
				text = text2.Substring(0, text2.Length - length);
			}
		}
		text = text.Trim();
		if (text.Length == 0)
		{
			return null;
		}
		bool? playing = null;
		lock (Sync)
		{
			if (array.Length >= 3)
			{
				AutomationElement prev = array[^3];
				AutomationElement playPause = array[^2];
				AutomationElement next = array[^1];
				_prev = prev;
				_playPause = playPause;
				_next = next;
				string name2 = _playPause.Current.Name;
				playing = name2.StartsWith("Приост", StringComparison.OrdinalIgnoreCase) || name2.StartsWith("Pause", StringComparison.OrdinalIgnoreCase);
			}
			else
			{
				_prev = (_playPause = (_next = null));
			}
		}
		int num = text.IndexOf(" — ", StringComparison.Ordinal);
		if (num <= 0)
		{
			return new Found(tabTitle, text, "", array.Length >= 3, playing);
		}
		return new Found(tabTitle, text.Substring(num + 3).Trim(), text.Substring(0, num).Trim(), array.Length >= 3, playing);
	}
}
