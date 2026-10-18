using System.Windows;
using ShortcutList.Services;
using Forms = System.Windows.Forms;

namespace ShortcutList.Views;

public partial class MainWindow
{
    internal void ActivateFromRequest(ActivationCommand command)
    {
        if (_isExitRequested) return;
        if (command == ActivationCommand.Settings) ShowHotkeySettings();
        else if (command == ActivationCommand.Show) ShowQuickSearch();
    }

    private bool ActivateExistingDialog()
    {
        var dialog = Forms.Application.OpenForms.Cast<Forms.Form>().LastOrDefault(x => x.Visible && x.Modal);
        if (dialog is null) return false;
        dialog.Activate();
        return true;
    }

    private void RequestExit()
    {
        if (_isExitRequested) return;
        _isExitRequested = true;
        _quickSearchWindow?.Hide();
        _notifyIcon?.ContextMenuStrip?.Close();
        CloseDialogsForShutdown();
        // Let nested WinForms dialog callbacks unwind before releasing the profile lock.
        _ = Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown()));
    }

    private void CloseDialogsForShutdown()
    {
        foreach (var form in Forms.Application.OpenForms.Cast<Forms.Form>().ToArray().Reverse())
        {
            if (form.IsDisposed) continue;
            form.DialogResult = Forms.DialogResult.Cancel;
            form.Close();
        }
    }

    private void ApplyHotkeys() => ApplyHotkeys(_store.Settings.EnableGlobalHotkeys, _store.Settings.QuickSearchHotkey);

    private void ApplyHotkeys(bool enabled, string? name)
    {
        if (_isExitRequested || _hotkeyRegistration is null) return;
        _hotkeyResult = _hotkeyRegistration.Apply(enabled, name);
        if (_notifyIcon is not null)
            _notifyIcon.Text = !_hotkeyResult.Enabled ? "DHub - ホットキー無効（トレイから操作）" :
                _hotkeyResult.HasFailure ? "DHub - ホットキー登録失敗（設定から再試行）" : "DHub - " + HotkeyBinding.Resolve(name).Name;
    }

    // This recovery path remains usable even when the data profile is read-only.
    private void ShowHotkeySettings()
    {
        if (_isExitRequested) return;
        if (_hotkeySettings is not null) { _hotkeySettings.Activate(); return; }
        _notifyIcon?.ContextMenuStrip?.Close();
        _quickSearchWindow?.Hide();
        using var form = new Forms.Form { Text = "DHub ホットキー設定 / 復帰", Width = 580, Height = 350,
            StartPosition = Forms.FormStartPosition.CenterScreen, MinimizeBox = false, MaximizeBox = false };
        _hotkeySettings = form;
        try
        {
            var hint = new Forms.Label { Left = 16, Top = 16, Width = 535, Height = 44,
                Text = "普段はトレイのみ。キーで一覧を呼び出し、選択・Esc・画面外クリックで隠れます。\nキーが使えないときも、トレイまたはDHubの再実行で戻れます。" };
            var enabled = new Forms.CheckBox { Left = 16, Top = 68, Width = 300, Text = "グローバルホットキーを有効にする", Checked = _store.Settings.EnableGlobalHotkeys };
            var choices = new Forms.ComboBox { Left = 16, Top = 102, Width = 260, DropDownStyle = Forms.ComboBoxStyle.DropDownList };
            choices.Items.AddRange(HotkeyBinding.Choices.Select(x => (object)x.Name).ToArray());
            choices.SelectedItem = HotkeyBinding.Resolve(_store.Settings.QuickSearchHotkey).Name;
            var status = new Forms.Label { Left = 16, Top = 140, Width = 535, Height = 65 };
            void UpdateStatus() => status.Text = !_hotkeyResult.Enabled ? "ホットキーは無効です。トレイのメニューから操作できます。" :
                (_hotkeyResult.SearchRegistered ? "一覧: 登録できました。" : "一覧: 登録できません。他のアプリとの重複などが考えられます。別のキーを選ぶか、競合アプリを閉じて再試行してください。") +
                "\n" + (_hotkeyResult.ManagementRegistered ? "管理画面: Ctrl+Shift+D" : "管理画面: Ctrl+Shift+Dの登録に失敗しました。トレイから開けます。");
            var retry = new Forms.Button { Left = 16, Top = 214, Width = 180, Height = 32, Text = "今回だけ適用 / 再試行" };
            retry.Click += (_, _) => { ApplyHotkeys(enabled.Checked, choices.SelectedItem?.ToString()); UpdateStatus(); };
            var save = new Forms.Button { Left = 206, Top = 214, Width = 155, Height = 32, Text = "保存して適用", Enabled = !_store.IsReadOnly };
            save.Click += (_, _) =>
            {
                var previousEnabled = _store.Settings.EnableGlobalHotkeys;
                var previousKey = _store.Settings.QuickSearchHotkey;
                try
                {
                    _store.Settings.EnableGlobalHotkeys = enabled.Checked;
                    _store.Settings.QuickSearchHotkey = choices.SelectedItem?.ToString() ?? HotkeyBinding.DefaultName;
                    _store.SaveChanges();
                    ApplyHotkeys();
                    UpdateStatus();
                    status.Text += "\n保存しました。";
                }
                catch (Exception ex)
                {
                    _store.Settings.EnableGlobalHotkeys = previousEnabled;
                    _store.Settings.QuickSearchHotkey = previousKey;
                    status.Text = "保存できませんでした。保存データの確認 / 復旧を行ってください。\n" + ex.Message;
                }
            };
            var close = new Forms.Button { Left = 371, Top = 214, Width = 80, Height = 32, Text = "閉じる", DialogResult = Forms.DialogResult.Cancel };
            var exit = new Forms.Button { Left = 461, Top = 214, Width = 80, Height = 32, Text = "終了" };
            exit.Click += (_, _) => { form.Close(); RequestExit(); };
            var note = new Forms.Label { Left = 16, Top = 258, Width = 535, Height = 42,
                Text = _store.IsReadOnly ? "保存データを確認するまで設定の保存はできません。今回だけのキー再登録は可能です。" : "閉じるだけでは終了しません。次回の起動は画面を表示せず待機します。" };
            form.Controls.AddRange(new Forms.Control[] { hint, enabled, choices, status, retry, save, close, exit, note });
            form.CancelButton = close;
            UpdateStatus();
            form.ShowDialog();
        }
        finally { _hotkeySettings = null; }
    }
}
