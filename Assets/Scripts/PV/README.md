# PV Capture

既存ライブ素材を参照する、Unity Editor向けの撮影専用Sceneです。

## 起動

1. Play Modeを終了します。
2. `Assets/Scenes/PV_Capture.unity` を単独で開きます。本編とのAdditive起動には対応しません。
3. `Tools > PV Capture > Open Controls` で操作パネルを開きます。
4. UnityのPlayを押します。素材・音声データの読み込み後、ライブ全体が1倍速で開始します。

編集時のSceneにはカメラ・制御・非アクティブの環境テンプレートだけがあります。ステージとUnityちゃんはPlay Modeで生成されます。

## カメラ

Gameビュー内の右クリックでマウスをロックして操作を開始します。

| 操作 | 内容 |
| --- | --- |
| WASD | 前後左右 |
| Q / E | 下降 / 上昇 |
| マウス | 視線回転 |
| Shift | 高速移動 |
| ホイール | 移動速度変更 |
| ESC | ロック解除。Play Modeは終了しません |

操作パネルまたはInspectorからFOV、移動速度、回転感度、平滑化を変更できます。カメラはライブPause中も操作できます。別アプリへ切り替えた場合はカーソルを解除し、再生中のライブをPauseします。復帰後はResumeを押してください。

## ライブ操作

`PV Capture` パネル、または `PV Capture Controls` オブジェクトのInspectorで操作します。

- **Play**: Readyから、プリロールを含むライブ全体を開始。
- **Pause**: 音楽・ダンス・口パク・観客・ステージ演出を一緒に停止。
- **Resume**: Pauseした地点から全体を再開。
- **Reset**: 音源を止め、生成したライブ全体を作り直し、0秒のReadyへ戻す。カメラと保存済みPresetは維持。準備が終わったらPlayで次のテイクを開始。

楽曲終了時にはリザルトSceneへ移動せず、撮影Sceneで停止します。

## カメラPreset

操作パネルに10枠あります。**Save**で現在のPosition・Rotation・FOVを保存し、**Recall**で呼び出します。ラベルも編集できます。

保存先は `Assets/Scripts/PV/PVCameraPresets.asset`。SaveはPlay Mode中でもこの専用assetへ書き込むため、Play Mode終了後も残ります。通常のPlay Mode中のCamera編集は保存されないため、残したいアングルは必ずSaveしてください。初期Presetは調整用の出発点です。

## 1920×1080収録

Gameビューを16:9または1920×1080に設定します。導入済みUnity Recorderを使う場合はGame View入力・1920×1080を選択します。**固定シミュレーション時間を強制する設定は使わず、リアルタイムで収録してください。** 音声DSP時計と映像の進行を一致させるためです。負荷が高い場合はフレーム落ちが起こり得ます。

操作パネルはEditor専用なので映像には写りません。Show capture UIは撮影用ライブ配下のCanvasだけを切り替えます。本編の評価・音声認識UIは生成しません。

## 同期と既存素材

- 音源は既存MusicPlayerの全AudioSourceを同じDSP時刻へ予約再生。
- ダンス・口パクは既存Animator ControllerをManual PlayableGraphで駆動し、同じDSP経過時間を渡します。個別のSeek／速度変更APIは公開していません。
- 本編のStartMusicイベント時刻2.0166667秒と先行補正約0.23秒を使用。ステージ起動イベントはライブ2.5秒です。
- Reaktion、ステージのAnimator、紙吹雪・レーザーなどは既存素材を参照。Pauseでは音声時計・TimeScale・既存挙動を停止し、髪や表情の表示姿勢も保持します。
- Resetはライブを再生成します。音声解析状態・スポーナー・一時オブジェクトが前のテイクから残らない構成です。
- 共有Scene・Script・Prefab・Controllerを編集しません。環境の照明・反射・観客配置だけは作成時のTestSceneから撮影Sceneへ引き継いだ配置です。本編のScene配置変更は自動反映されません。
- PCカメラを使い、XRリグや本編StageDirectorは生成しません。自動生成される既存VRポーズメニューは撮影開始時に破棄します。

## 制限

- 任意時間へのSeek、0.25x／0.5x再生、逆再生は未実装です。目的の場面まで通常再生してPauseする運用です。
- 紙吹雪・レーザー・まばたき等の乱数による見た目は、テイク間で完全一致する保証はありません。
- GlobalのTimeScaleとAudioListener.pauseを撮影セッション中だけ使用します。Play Mode終了時に元の値へ戻します。他のSceneと同時に動かさないでください。
- プロジェクトのXR起動設定は維持するため、ヘッドセット未接続のOculus初期化警告が出る場合があります。撮影カメラ自体はXR描画を無効化しています。
- 録画ファイルの自動作成・エンコードやQuestの実プレイ映像収録は含みません。

## 検証

Play Modeで `Tools > PV Capture > Validate Playback (Play Mode)` を選ぶと、プリロールPause、音源とライブ時計、骨とParticleの停止、Resume、Reset後の再生を確認します。結果と1920×1080の確認画像は `Temp/PVCapture/` に出力します。検証ではライブをResetし、カメラを一時的に動かして元へ戻すため、収録中には実行しないでください。
