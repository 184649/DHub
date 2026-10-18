using System.Windows;
using ShortcutList.Models;
using ShortcutList.Services;

namespace ShortcutList.Views;

public partial class MainWindow
{
    private void AddSamplesButton_Click(object sender, RoutedEventArgs e) => AddSampleBookmarks();

    private void AddSampleBookmarks()
    {
        if (!EnsureEditingAllowed()) return;
        if (AskYesNo("次の3件を追加して、一覧から試しますか？\n\n" +
            "・使い方メモ: テキストを既定のアプリで開く\n" +
            "・作業フォルダー: 資料フォルダーを開く\n" +
            "・Windows ヘルプ: Microsoft公式サイトをブラウザーで開く\n\n" +
            "既存登録・同じサンプルは置き換えません。新しく追加するサンプルだけをお気に入りにします。\n" +
            "ファイルやURLは、ボタンを自分でクリックするまで開きません。\n" +
            "不要になったら一覧で選んで「削除」。元ファイルは残ります。\n\n" +
            "サンプルファイルの保存先:\n" + SampleBookmarks.DirectoryPath(_store.StorageFolder)) != true) return;

        IReadOnlyList<ShortcutItem> added;
        try { added = _store.AddSampleBookmarks(); }
        catch (Exception ex)
        {
            ApplyFilter();
            ShowInfo("サンプルを追加できませんでした。画面の登録は変更前に戻しています。\n" +
                "途中まで保存された可能性があるため、作成した専用サンプルファイルは残します。\n" +
                "「保存データの確認 / 復旧」で保存状態を確認してください。\n" + ex.Message);
            return;
        }
        try
        {
            RefreshFilterItemsKeepSelection();
            if (added.Count > 0)
            {
                SearchTextBox.Text = string.Empty;
                FilterComboBox.SelectedItem = AllFilter;
                GroupFilterComboBox.SelectedItem = AllGroupsFilter;
                TagFilterComboBox.SelectedItem = AllTagsFilter;
            }
            ApplyFilter();
            if (added.Count > 0)
            {
                ShortcutListView.SelectedItem = added[0];
                ShortcutListView.ScrollIntoView(added[0]);
            }
            RefreshTrayMenu();
            _ = RefreshLinkStatusAsync(false);
            ShowInfo(added.Count > 0
                ? $"サンプルを {added.Count} 件追加しました。ホットキーで一覧を開くか、トレイから選んで試せます。\n一覧の「削除」は登録だけを削除し、ファイルやフォルダーは残します。"
                : "同じサンプルまたは起動方法がすでにあるため、登録は追加していません。編集内容やお気に入りの設定もそのままです。\nトレイにない項目は、一覧で選んで「お気に入りに追加」で表示できます。");
        }
        catch (Exception ex)
        {
            ShowInfo("サンプルの登録処理は完了しましたが、画面の更新に失敗しました。\n登録を再追加せず、管理画面を開き直してください。\n" + ex.Message);
        }
    }

}
