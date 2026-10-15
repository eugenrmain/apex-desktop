using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace Apex;

public sealed partial class MainWindow
{
    private enum NoticeKind
    {
        Success, Warning, Information, Error
    }
    private (string Message, NoticeKind Kind)? taskCompletion;
    private Border? taskNotice;
    private DispatcherQueueTimer? noticeTimer;
    private DispatcherQueueTimer? noticeFade;
    private void FadeTaskNotice()
    {
        noticeTimer?.Stop();
        if (taskNotice is null)
            return;
        if (!uiSettings.AnimationsEnabled)
        {
            DismissTaskNotice();
            return;
        }
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        noticeFade = DispatcherQueue.CreateTimer();
        noticeFade.Interval = TimeSpan.FromMilliseconds(16);
        noticeFade.Tick += (_, _) => { if (taskNotice is null) { DismissTaskNotice(); return; } double t = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / 350; taskNotice.Opacity = Math.Clamp(1 - t, 0, 1); if (t >= 1) DismissTaskNotice(); };
        noticeFade.Start();
    }
    private void HoldTaskNotice()
    {
        noticeTimer?.Stop();
        noticeFade?.Stop();
        noticeFade = null;
        if (taskNotice is not null)
            taskNotice.Opacity = 1;
    }

    private void FinishTask(string message, int skipped = 0)
    {
        status.Text = message;
        taskCompletion = (message, skipped > 0 ? NoticeKind.Warning : NoticeKind.Success);
    }

    private void ShowTaskNotice(string title, string message, NoticeKind kind, bool sound = true)
    {
        DismissTaskNotice();
        var body = new Grid { ColumnSpacing = 12 };
        body.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        body.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var glyph = kind switch
        {
            NoticeKind.Success => "\uE73E",
            NoticeKind.Warning => "\uE7BA",
            NoticeKind.Error => "\uEA39",
            _ => "\uE946"
        };
        body.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Background = Brush("#2480C7FF"), Child = Icon(glyph, 17, "#B0E9FF"), VerticalAlignment = VerticalAlignment.Top });
        var copy = new StackPanel { Spacing = 5 };
        copy.Children.Add(Text(title, 15, "#EDF3FF", true));
        var detail = Text(message, 13, "#B5C5DE");
        detail.MaxLines = 3;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(detail, message);
        copy.Children.Add(detail);
        Grid.SetColumn(copy, 1);
        body.Children.Add(copy);
        var close = new Button { Content = Icon("\uE711", 11, "#BDCCE5"), Width = 28, Height = 28, Padding = new Thickness(0), CornerRadius = new CornerRadius(8), Background = Brush("#13FFFFFF"), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
        Automation(close, "Dismiss task notification");
        close.Click += (_, _) => DismissTaskNotice();
        Grid.SetColumn(close, 2);
        body.Children.Add(close);
        taskNotice = new Border
        {
            Width = Math.Min(performancePopup is null ? 370 : 520, Math.Max(200, root.ActualWidth - 48)),
            HorizontalAlignment = performancePopup is null ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = performancePopup is null ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            Margin = performancePopup is null ? new Thickness(24, 24, 28, 52) : new Thickness(24, 64, 24, 24),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(18),
            Background = Gradient("#FA101B33", "#FA0B1429", "#FA1B1639"),
            BorderBrush = Gradient("#709EDFFF", "#35FFFFFF", "#428982FF"),
            BorderThickness = new Thickness(1),
            Child = body
        };
        Automation(taskNotice, title + ": " + message);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(taskNotice, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Grid.SetRowSpan(taskNotice, 3);
        Canvas.SetZIndex(taskNotice, 140);
        root.Children.Add(taskNotice);
        taskNotice.Loaded += (sender, _) => Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement((FrameworkElement)sender)?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
        noticeTimer = DispatcherQueue.CreateTimer();
        noticeTimer.Interval = TimeSpan.FromSeconds(kind == NoticeKind.Error ? 6 : kind == NoticeKind.Warning ? 5 : 3);
        noticeTimer.IsRepeating = false;
        noticeTimer.Tick += (_, _) => FadeTaskNotice();
        taskNotice.PointerEntered += (_, _) => HoldTaskNotice();
        taskNotice.PointerExited += (_, _) => noticeTimer?.Start();
        taskNotice.GotFocus += (_, _) => HoldTaskNotice();
        taskNotice.LostFocus += (_, _) => noticeTimer?.Start();
        noticeTimer.Start();
        if (kind == NoticeKind.Success && performancePopup is null)
            PlayTaskFlare(taskNotice);
        if (sound && kind == NoticeKind.Success && preferences.CompletionSounds)
            PlayCompletionSound(Path.Combine(AppContext.BaseDirectory, "Assets", "Complete.wav"), 0, 0x0001 | 0x0002 | 0x20000);
    }

    private void DismissTaskNotice()
    {
        noticeTimer?.Stop();
        noticeTimer = null;
        noticeFade?.Stop();
        noticeFade = null;
        StopTaskFlare();
        if (taskNotice is not null)
            root.Children.Remove(taskNotice);
        taskNotice = null;
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlayCompletionSound(string? sound, nint module, uint flags);
}
