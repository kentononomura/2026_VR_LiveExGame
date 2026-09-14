# Androidビルド警告の確認（2026-09-14）

確認元: 添付画像とプロジェクト内の `Logs/Editor.log`。

## 修正

- Active Input HandlingをBoth（2）からInput System Package（1）へ変更。
- TitleVoiceManager、VoiceReactionManager、SceneTransitionManager、WaitingRoom、StageDirector、UnityChanサンプル、Reaktionの旧Input呼び出しを `ProjectInput` へ接続。Keyboard/Mouse/Gamepadから読むため、新入力方式のみでも旧APIのInvalidOperationExceptionを発生させない。既存のXR InputAction・XRデバイス取得は維持。
- キーボードがないQuestではPC用キーを未入力として扱う。プロジェクトの標準ボタン名（Jump / Fire1～3 / Submit / Cancel）と標準軸を対応付け。InputManagerに将来追加する独自軸はこのヘルパーにも定義が必要。
- PVCaptureControllerとPVCaptureValidationの非推奨FindObjectsSortMode引数を削除。順序を使用しない検索のため、処理内容は同じ。
- AndroidDebugSymbolsSetupがEditor読み込み時とAndroidビルド前に、シンボル設定Noneの場合のみSymbolTableへ変更。既にFullなら維持。診断データを無効化するのではなく、スタックトレースの解決に必要なシンボルを生成する。
- XR Interaction Toolkitがサンプルキャッシュ確認時に要求するAssets/Samplesディレクトリを追加。

## 残る通知

- URP 17.5.0のShaderVariablesFunctions.hlsl内の整数剰余について、Vulkanシェーダーコンパイラーが出す性能警告。Unity管理パッケージ由来。PackageCacheの一時編集や描画API変更で隠すことはしていないため、再ビルド時に残る可能性がある。
- HMD未取得時の目線高フォールバック、マイク音量が長時間ほぼゼロの通知は、接続状態や実入力の診断。警告を消すための無条件抑制はしていない。
- TextMeshProの大きな関数を独立したcppファイルに分けるIL2CPP通知は、C#のコンパイルエラーではない。

## 検証・再確認

旧入力のコンパイル定義を除外した状態で、Editor向け・Player向け・Editor拡張のC#コンパイルを確認。修正後のコンパイル警告なし。
APKの再ビルドとQuestでの入力確認は未実施。

Unityの入力バックエンド設定反映にはEditor再起動が必要になる。シーンを保存して再起動後、ConsoleをClearしてからビルドし、新しく出た警告を確認する。
TitleSceneで左右トリガー、TestSceneで左トリガー、PCではEnter/Spaceなどのデバッグキーを確認する。既存ログの警告は修正後も履歴として残る。
