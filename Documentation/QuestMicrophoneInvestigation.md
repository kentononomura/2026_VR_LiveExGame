# Quest 2 タイトル画面のマイク入力調査（2026-09-14）

## 確認できたこと

- クリーンビルド後、Voskモデル読み込みは完了するが、タイトルのマイク表示が待機のままで声に反応しないとの報告。
- 生成済みAndroidManifestに `RECORD_AUDIO` があり、接続中のQuest 2でも対象アプリの同権限は `granted=true`。
- 調査時の `adb shell dumpsys audio` は `mic mute FromSwitch=false FromRestrictions=false FromApi=true from system=true`。ただし、その時点で対象アプリのプロセスは動いておらず、テスト当時のミュート状態を証明するものではない。
- 過去の録音履歴には対象アプリの `VOICE_RECOGNITION` 開始・停止がある。実際の音声波形は確認できていない。
- 既存タイトル処理はAudioClipが返れば起動済みとし、その後録音位置が進まなくても再試行しなかった。
- タイトルの音量メーターはトリガーと独立。音声でゲームを開始する認識処理は左右どちらかのトリガーを押している間に有効。

## 変更

- タイトルで録音位置が5秒間進まない場合、マイクと録音クリップを解放し、2秒後に再試行。
- アプリ中断時に録音を解放し、復帰後に再開。再接続前の認識結果を世代番号で無効化。
- マイク開始例外とGetData失敗を処理し、再試行できるように変更。
- OSミュート状態を毎秒読み取り、タイトルに表示。権限待ち・再接続・低音量または無音の表示も分離。
- OS設定は変更しない。状態取得に失敗した場合は「不明」（null）とし、解除済みとは判定しない。

OS状態確認はAndroidの [AudioManager.isMicrophoneMute](https://developer.android.com/reference/android/media/AudioManager#isMicrophoneMute()) を使用。

## 検証

- Unity 6000.5.1f1の既存コンパイル応答ファイルを使い、Android Player条件とEditor条件でAssembly-CSharpを再コンパイル。両方成功。別ファイルPVCaptureControllerの既存非推奨API警告のみ。
- 出力先は `Temp/MicrophoneValidation`。Unityの通常のビルド成果物は置換していない。
- 差分の空白チェック成功。
- APKの再ビルド・再インストール・実機発話テストは未実施。今回の変更で現象が解消するかは未確認。

## 次回の実機確認

1. 更新版をビルド・インストールし、タイトルのモデル準備完了後の状態文言を確認。
2. トリガーを押さずに話し、音量メーターが動くか確認。
3. 「QuestのOSでマイクミュート中」が出た場合、Quest側でミュート状態を切り替えて表示が追従するか確認。
4. 音量メーターが動いたら、左右どちらかのトリガーを押しながら「ライブスタート」と発話。
5. スリープ・復帰後も入力が戻るか確認。失敗時は同じ実行中に `adb logcat -d -s Unity` と `adb shell dumpsys audio` を採取する。
