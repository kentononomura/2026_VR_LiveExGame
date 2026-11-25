# リアクション Avatar 調査（2026-11-25）

## 原因と修正

Reaction フォルダの新しい FBX を Humanoid 化した際、アニメーション先頭の骨回転を基準姿勢として設定していた。元の Generic アニメーションは正常でも、実際の unitychan_hw への Humanoid リターゲットで腕がずれる状態だった。単独の AnimationClipPlayable でも発生し、タイムラインのミキサーを通す前に再現した。

3 FBX の .meta の humanDescription.skeleton を修正。元 FBX の骨位置とスケールを保持し、同名ボーンの基準回転を Assets/UnityChan/unitychan_hw/unitychan_hw.fbx の基準姿勢に合わせた。Human ボーン対応も同モデルに合わせ、元 FBX に存在しない目の locator は除外した。FBX のモーションデータ自体は変更していない。主原因は基準回転であり、ボーン対応の統一だけによる腕の誤差改善は見られなかった。

## 検証

Unity 6000.5.1f1 上で各クリップを開始から終了まで61時点サンプリング。元 FBX の一時 Generic コピーと、ゲームで使用する unitychan_hw.prefab に単独再生した Humanoid を比較した。

| クリップ | 胸を基準とする腕の方向差（最大） | 胸を基準とする手の姿勢差（最大） |
| --- | ---: | ---: |
| Heart v08 | 1.911° | 2.427° |
| Kyururin Piece v08 | 2.018° | 2.164° |
| Pointing v03 | 3.396° | 3.539° |

Humanoid は腕のねじりをボーン間で再配分するため、ローカル回転単体の差は方向の破綻を示す指標にはならない。上記は上腕・前腕の実際の方向、および手の胸に対する姿勢を測定した値。完全一致を保証するものではない。比較画像でも Heart と Pointing の腕の方向を確認した。

全身レイヤーと単独再生の腕の差は0°。ダンス時刻10/30/60秒、反応の25/50/75%の組み合わせで、上半身レイヤーの腕の差、脚のダンスからの差、重み0で復帰した腕の差はいずれも0°。重み0/0.25/0.5/0.75/1で非有限の回転値なし。3 Avatar はすべて valid / Human。

## 再検証

編集モードで Tools > Reaction Timeline > Audit Refined Avatars を実行。Assets/Editor/ReactionAvatarAudit.cs が一時コピーを作成・削除し、結果を Temp/ReactionAvatarAudit/audit.txt、比較画像を同ディレクトリの Images に出力する。方向差または手の姿勢差が5°超、レイヤー比較差が0.2°超なら失敗する。このしきい値は今回の3クリップの回帰確認用。

目視確認は Assets/Scenes/ReactionTimeline_Test.unity を Play し、各リアクションを選択して Trigger。Full body をオフにすると上半身モードでダンスの脚を維持する。設定は Trigger 時点で取り込まれるため、切り替えたら再度 Trigger する。
