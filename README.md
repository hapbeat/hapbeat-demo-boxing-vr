# Hapbeat Boxing

Unity **6000.3.12f1** の1ラウンド55秒のVRスパーリングデモ。左右のグローブで攻撃・ガードし、頭を動かして相手のパンチを避けます。起動後の最初だけ短いチュートリアル（4ステップ）を行い、3カウント後にラウンドが始まり、終了時に得点と再開メニューを表示します。Hapbeatが未接続でもゲームは動作します。

スコアボードには自分と相手のHPバーを表示します。初期HPは各100、プレイヤーの攻撃は準備状態とHMD相対速度に応じて0.5〜20、敵の通常ヒットは10・強ヒットは20ダメージ、グローブ・腕でのガードはダメージなしです。HPが0になるとKOで終了し、再スタート時に全回復します。ガード成立時は相手のパンチを接触点で止め、そこから引き戻します。攻撃のターン制限はありません。

## 入手と起動

- Unity **6000.3.12f1**（Android Build Support付き）で開きます。
- `git clone https://github.com/hapbeat/hapbeat-demo-boxing-vr.git` したフォルダーをそのままUnity Hubで開けば動作します。Hapbeat SDK（[hapbeat-unity-sdk](https://github.com/hapbeat/hapbeat-unity-sdk)）とDemo Switch packageは `Packages/manifest.json` に固定したGit URLからUnity Package Managerが取得します。
- Windowsでは短いパス（例: `C:\dev\hapbeat-demo-boxing-vr`）にcloneしてください。深い階層に置くと、Unityのパッケージ読込がWindowsのパス長上限（260文字）を超えてインポートに失敗することがあります。
- ビルド済みAPKは [GitHub Releases](https://github.com/hapbeat/hapbeat-demo-boxing-vr/releases) で公開します。Questへは `adb install -r HapbeatBoxing.apk` 等でインストールしてください。
- メニューで表示する半透明の手は、このリポジトリでは自作の簡易モデル（球・カプセルの組合せ、`Assets/Boxing/Art/PlaceholderHands/`）です。Unity XR Handsサンプルの手メッシュはUnity Package Distribution Licenseによりソース資産として再配布できないため含めていません。関節名はXR Handsと同じなので、追跡・ピンチ・メニュー操作は同じように動作します。簡易モデルは `./tools/run-unity.ps1 -Task Hands` で再生成し、Sceneへ手の設定を再適用できます。
- Hapbeatの開発ワークスペースでは `./tools/link-workspace.ps1` を一度実行すると、非公開の手メッシュ（`Assets/HapbeatPrivate/`）と、SDK・Demo Switchのローカルソース（`Packages/` 内の埋め込みpackage。manifestのGit URLより優先）をジャンクションで接続します。実行時に `BoxingHandModelResolver` が非公開メッシュを見つければそれを使い、無ければ簡易モデルに切り替えます（Consoleの `[Boxing Hands]` に使用したモデルを表示）。

## Editor + XR Interaction Simulator（HMD不要）

1. `Hapbeat Boxing > Editor Input > Simulator` を選び、Playを押します。Gameビューをクリックしてキーボード入力を渡してください。
2. ゲームの入力は仮想 `Controllers` を使います。キーボードで拳を直接動かすDesktopモードはありません。
3. `Hapbeat Boxing > Editor Input > Controls` に操作ガイドがあります。TabでFPS／デバイス操作を切替、Hで頭、`[`／`]`で左右デバイスを選択し、WASDで移動、Q/Eで上下移動、右マウスドラッグ／矢印キーで回転、Rでリセットします。仮想コントローラーの1がA/X、2がB/Yです。Escapeでゲームメニュー、Enterで決定もできます。

Unity XRI 3.3.1の標準シミュレーターPrefab・入力設定を使用します。独自のテスト姿勢をゲームへ直接渡す機能ではなく、実機と同じInput System経由で頭・両手・ボタンを読みます。操作ガイドはEditorウィンドウに表示し、サンプルの装飾UIは取り込んでいません。

シミュレーター選択時はEditorのネイティブOpenXR起動を無効にします。Air Linkへ戻す時はPlayを止め、`Hapbeat Boxing > Editor Input > Air Link` を選んでください。選択はこのPC・このプロジェクト専用です。AndroidのOpenXR設定は変えず、シミュレーター資産は `Assets/Boxing/Editor/Simulator` に隔離してAPKへ含めません。

## Editor + Quest Air Link

1. この `boxing-vr` フォルダーをUnity 6000.3.12f1で開き、`Assets/Boxing/Scenes/Boxing.unity` を開きます。
2. Meta Horizon LinkでQuestをAir Link接続します。WindowsのOpenXR runtimeはMeta Horizon Linkを選択してください。
3. `Hapbeat Boxing > Editor Input > Air Link` を選び、Unityのactive build targetをWindowsにしてPlayを押します。起動時メニューの `START 55s ROUND` を選択します。

手追跡（Hands）が既定です。メニューでは半透明の手を表示し、親指と人差し指の中点から伸びるレイで項目を指し、つまんで決定します。Meta Aimの照準方向・ピンチ入力を使用し、対応デバイスが存在しない環境では実測の手首方向と指先間隔を使います。Meta Aimが存在するが無効／システムジェスチャー中の場合は選択を止めます。視線だけでは選択されません。コントローラーは不要です。起動時のHMD位置・向きをリング上の開始位置に合わせます。必要ならメニューから `RECENTER` を選択します。

レイの起点はXRI Hands Interaction DemoのPinchPointFollowと同じ中点方式です。メニュー項目の当たり判定はデモ独自実装を維持しています。手の表示はXRHandSkeletonDriverで関節を動かし、試合中はグローブへ戻ります。公開リポジトリでは簡易モデルを使います（[入手と起動](#入手と起動)）。Unity XR Handsの標準手モデルを接続したビルドでは、そのモデルに `Assets/Boxing/Art/UnityHands/LICENSE.md`（Unity Package Distribution License）が適用されます。

手が取れない場合は、追跡された左右Touchを持ってA/X・Menu/B/Yまたはスティックを操作すると一時的にコントローラー入力を使います。A/Xで決定、左右どちらのスティックでも上下で選択、Menu/B/Yでメニューを開閉します。両手の手追跡が復帰するとHandsへ戻ります。INPUT欄は現在のソースを表示し、選択するとHands優先へ戻します。視線だけでDesktopやControllers固定へ切り替わることはありません。

周囲の物を片付け、現実の物体や人を殴らない範囲で試してください。コントローラーのストラップを使い、強く振り切らず弱いパンチから確認します。移動距離・速度の応答は力の測定ではなくゲーム用の演出です。

XR Handsの手首の位置・向きから固定グローブを動かします。指先はパンチ・ガードの入力成立条件にせず、握り判定も不要です。左手を開いて顔の前で掌を自分に向け、0.8秒保持するとメニューを開閉します。メニューのジェスチャー・レイ・ピンチには実測の指関節を使用し、推定姿勢は使いません。片手だけでもメニュー操作は可能です。追跡復帰時やメニュー表示時にピンチしたままでも決定されず、一度指を離してからつまみ直します。Meta Horizon Linkの手追跡利用には対応する開発機能を有効にする必要があります。追跡不能時・切替直後には攻撃判定を停止し、復帰移動をパンチとして扱いません。

Meta公式: [Link開発設定](https://developers.meta.com/horizon/documentation/unity/unity-link/)、[手追跡](https://developers.meta.com/horizon/documentation/unity/unity-handtracking-overview/)。

## 操作と調整

### 開始位置・デバッグ移動

開始マーカーはリング前方へ0.2mです。相手までの前後間隔は基準約0.88m、グローブ中心で接触するまでの前後移動は約0.62mです。相手はパンチ時に上体を前へ運びます。開始時とRECENTER時にはマーカーのXZ位置・向きに頭を合わせ、追跡した身長は維持します。Sceneの`Start position`で調整できます。

毎ラウンド開始時にHMD高さを再取得します。両手の追跡待ちでも更新し、HMD自体がまだ未追跡なら復帰時に再取得します。XR原点の床位置を環境・相手の足元へ適用し、HMDの床からの高さへ相手の頭を合わせ、スコア表示は目線の1.05m上へ設定します。試合中の屈伸で身長を再調整しません。物理的な床をHMDだけから新たに測定する機能ではないため、Quest側の床キャリブレーションそのものが誤っている場合はQuest側の再設定が必要です。

Controllersモードでは、メニューを閉じた追跡安定中に左スティックでデバッグ移動できます。マーカーの向きを基準に0.45m/s、開始点から0.6m以内、相手から0.5m以上を保ちます。メニュー中は従来どおり選択だけです。移動中は攻撃を無効化します。展示時は`BoxingInput.debugStickMovement`をOFFにしてください。RECENTERでデバッグ移動分も戻ります。

### 引き戻し・間隔と速度によるプレイヤーパンチ

左右それぞれ、HMDを差し引いた手の位置・速度を使います。静止保持や長いストロークは不要です。頭と手を同じだけ動かす歩行は強度になりません。

1. HMDから70cm以内へ手を戻し、直前接触よりHMDへ10cm近づける（または接触位置から14cm移動する）と次の強打を準備します。戻してすぐ打てます。
2. 引き戻せなくても、同じ手の接触から1秒経てば再準備します。左右は独立です。連続接触・相手のガードへの接触でもその手の準備を消費します。
3. 準備済みならHMD相対速度0.45m/sから強打（約14.5ダメージ）、2.5m/sで最大20ダメージ。速度は短時間平滑化し直近180msのピークを使います。準備なし／低速の接触は0.5ダメージです。背後へ引く必要はありません。
4. 追跡喪失、メニュー、リセンターでは履歴を破棄します。`BoxingTuning`のPlayer punch response欄で調整できます。接触し続けている間は再ヒットしません。

プレイヤーのダメージ・音・触覚の強度はこの同一判定を使います。敵の攻撃は既定の速度判定を維持します。グローブ同士なら双方ともダメージなしです。

### 前腕のガード

NPCは左右の上腕・前腕にも、描画リグに追従する連続スイープ判定を持ちます。グローブ・腕・身体のうち最初に接触した面を採用し、腕で遮られたパンチは身体を傷つけません。腕への接触は得点上はグローブ接触（相手のガード）と同じ扱いで、音・触覚は頭・胴への命中と同じ `punch_body_*` です。NPCのグローブもプレイヤーと同じ幅15cm（接触判定の半径7.5cm）です。

プレイヤーにはグローブの手首方向へ伸びる長さ28cm・直径9cmの簡易前腕を表示します。実際の肘の推定ではなく、手首の向きに固定した防御用モデルです。前腕への接触も同じ左腕／右腕のガードとして処理し、受けた側の手首で `guard_arm_*` の音・触覚を鳴らします。前腕自体は相手にダメージを与えません。判定は既存の移動掃引方式（重なる13個の球）で、頭・胴・グローブと最初の接触を比較します。追跡喪失・メニュー中には判定せず、メニューでは前腕も非表示です。

### 外部操作への接続点とHands

`BoxingGame.RecenterPlayer()`、`UseHandTracking()`、`UseControllers()`、`StartRound()`が外部操作用のローカル呼び出し口です。Unityのメインスレッドで呼びます。現行のDemo Switchプロトコルはデモ切替のみなので、リセンター等のネットワーク命令は**未接続**です。

Hands処理およびStandalone/AndroidのHand Tracking Subsystemは有効です。起動後に手のサブシステムが使えるようになった場合も再取得します。`WAITING FOR HAND SUBSYSTEM`は動作中の手サブシステムがない状態、`WRIST TRACKING: LEFT ... / RIGHT ...`は各手首の取得状態です。Consoleの`[Boxing Input]`と`[Boxing State]`はソース・追跡・停止理由の変化を記録します。実機Handsの動作は自動入力テストでは保証しません。

- 開始位置・正面はシーンの `Arena - original procedural assets / Start position` で調整できます。PositionのX/ZとRotationのYを使い、既定の `(0, 0.003, 0.2)`・Y回転`0`は相手に正対します。高さはHMDの追跡値を維持します。`XR Origin (Boxing)` のBoxingInputにあるStart Pointが参照先です。Main Cameraを直接回転しても追跡値で上書きされます。初回追跡時・ラウンド開始時・RECENTER時にこの開始位置へ合わせます。
- Air Link中はPCのGameビューではなくOpenXRセッションのフォーカスを使って停止判定します。`HEADSET PAUSED - OPENXR NOT FOCUSED` はQuestのシステム画面等でVR側のフォーカスがない状態、`APPLICATION PAUSED` はアプリ中断、`GAME WINDOW NOT FOCUSED` はネイティブXRがないSimulator側のウィンドウ非アクティブを表します。実際の追跡喪失・安全範囲逸脱による停止は維持します。
- プレイヤーの判定は左右グローブと頭。プレイヤーの腕は描画・判定しません。相手には見た目用の腕・脚があります。
- 相手は単発（ジャブ、クロス、リード／リアフック、ボディストレート、ボディフック）とコンビネーション（ジャブ-クロス、ジャブ-クロス-フック、ボディ-ヘッドのダブル、フック-クロス）、フェイント、踏み込み・後退・左右への回り込み、ガードでの待機を状況に応じて選びます。足を前後に置き、リズムのある小刻みなステップと膝の曲げ・小さな上体の揺れで待機します。パンチでは肩の回転・上体の前進・拳の軌道を連動させます。狙いは予備動作開始時に固定し、パンチ中は頭を追尾しません。
- 起動後、開始位置で追跡が安定して `autoStartSeconds`（2秒）たつとチュートリアルを始めます。スコアボードに手順を表示し、相手は台本どおりに動きます（自分の判断・回避なし）。1/4: 顔の前でガードした相手の顔を打つ（グローブに当たりダメージなし）。2/4: 同じガードのままお腹を打つ（肘が胴の上部の前に来てガードになるため、胴の中心から約0.1m下を狙います）。1・2では狙う位置に半透明の輪（表示のみで判定には使いません）を出します。3/4: 相手のストレートを顔で受ける（ガードすると打ち直し）。4/4: 少し遅いストレートを顔の前のグローブで防ぐ（当たると打ち直し）。成功すると結果を `tutorialMessageSeconds`（1.5秒、音声がそれより長ければ音声の終わりまで）表示して次へ進みます。1・2は `tutorialStepTimeoutSeconds`（20秒）、3・4は `tutorialStrikes`（3回）で、結果の表示なしに次へ進みます。4の後は「試合開始」を表示し、カウントダウンとゴングでラウンドを始めます。チュートリアル中のHP・得点はラウンド開始時にリセットします。再挑戦・やり直し・次のラウンド（`BoxingGame.StartRound`）ではチュートリアルを行いません。`tutorialOnStart` をOFFにすると従来どおり直接カウントダウンします。手順の指示・打ち直し・結果と「試合開始」は表示と同時に日本語の音声でも読み上げ（新しい音声は前の音声を止めます）、3・4の相手のパンチは指示・打ち直しの音声が終わってから出し、カウントダウンは「試合開始」の音声が終わってから始めます。文言は `Voice/voice-lines.json`、音声は `Assets/Boxing/Audio/Tutorial/<id>.wav` です。文言を変えたら hapbeat-demos の `python tools/tts/generate-voice.py unity/boxing-vr/Voice/voice-lines.json unity/boxing-vr/Assets/Boxing/Audio/Tutorial`（または `tools/tts/generate-all-voices.ps1 -Only boxing`）で、変わった行だけ再生成します（手順は `tools/tts/README.md`）。日本語の表示にはDemo Switch同梱のNoto Sans CJK JPを使います。
- メニュー表示、Questのシステムメニュー・フォーカス喪失、追跡喪失、開始点から1m以上離れた時は対戦時間・敵・触覚を停止します。システムメニューから戻った時は `RESUME` で明示的に再開します。
- `Assets/Boxing/BoxingTuning.asset` でラウンド時間、攻撃間隔、予備動作、判定サイズ、強打の再準備・速度、敵の速度→ゲインのカーブ・閾値を変更できます。
- `IMPACT: WeakHard` は弱（`_soft`）／強（`_hard`）のイベントを切り替えます。相手のパンチは接触の相対速度2.5m/s（`hardHitSpeed`）が境です。自分のパンチはダメージと同じパンチの強度で決め、強度0.9（`punchHardStrength`、振りのピーク約1.8m/s、ダメージ18以上）から強です。準備なし（強度0）の当たりは速くても弱です。`Continuous` は常に弱のイベントを使い、ゲインだけを連続変更します。接触速度0.25m/s未満は無視します。ゲインはプレイヤーのパンチが準備状態と振りの速さ、敵のパンチが接触速度（6m/sで最大）で決まります。
- 触覚・音のイベントは「当たった部位 × 弱／強」の10種類です（下の「触覚の作業」の表）。`Assets/Boxing/Haptics/BoxingEventMap.asset` は手首の8種を左右別（`left_<イベント>` / `right_<イベント>`）、首の2種をそのまま（`hit_body_soft/hard`）の18エントリーで持ちます。波形が複数あるイベントは、2つ目以降を `<エントリー>_2`、`_3` … のエントリーで持ちます。

## 相手のリグ付きモデル

相手はBlenderで制作したローポリボクサーで、小さな鼻・耳を持ちます。`Assets/Boxing/Art/Boxer.fbx` と20本の骨を使用し、元データは `art-source/boxing-opponent/` にあります。リセンター時のHMD高さへ頭中心を合わせ、通常のしゃがみでは縮尺を変えません。`BoxingOpponentAvatar` が拳・頭の判定位置へ骨格を合わせます。手首は前腕に沿わせ、反対の拳はガード位置を維持します。

グローブは同じ元メッシュから両者へ反映し、幅15cm・厚さ約13cm・全長約26cmへ統一しています。丸い打撃面・掌側へ折れた指のパッド・内側の親指・赤い幅広カフと細い白縁を備え、相手の体格を変えても実寸は一定です。左右の親指・掌・手首の向きはメッシュ上の目印で回帰検証します。球形の接触判定は直径14cm。これはデモの調整値であり、特定製品や全競技共通の寸法を再現したものではありません。相手は目線基準の高さを保ち、従来より幅12％・厚み10％を抑えています。

相手の打撃は頭ストレート・胴ストレート・頭フック・胴フックの4種類です。胴狙いでは構えを下げ、プレイヤーの胴の判定の軸（頭から約43cm下、前面から半径13cm奥）を狙います。頭狙いが頭の中心を狙うのと同じく、拳は伸び切る前の速いうちに胴の前面へ当たります。頭だけのガードでは胴体を防げません。

プレイヤーの胴の判定は、HMDの下25〜60cmを軸とする半径13cmの縦のカプセルです（`BoxingPlayerBody`）。軸はHMDから顔の水平な向きの後ろへ14cmずらすため、カプセルの前面は目の位置を通る鉛直面より1cm後ろにあり、頭をどう向けても前へ出ません。頭の上下の傾き・首のかしげは胴を動かしません。頭の向きが無効なとき（未追跡の初期値）は相手のいる+Z方向を正面とします。前腕を腹の前や脇腹に付けて構えると、胴打ちは胴より先に前腕へ当たってガードになり、腕を開くと胴に当たります。判定は前腕と同じ移動掃引方式（重なる13個の球）です。胴体被弾の触覚は、追加デバイスを要求せず既存の首側受信先を共用します。

### 相手のAI

`BoxingOpponent` は外部パッケージや機械学習を使わない自前の状態機械です。状態は待機（Neutral）・攻撃・防御・被弾硬直・カウンターの5つで、待機中は各行動を「基礎重み × 状況係数 + 小さな乱数」で採点して最大のものを選びます。直近3行動と同じ行動は減点し、乱数の種はラウンドごとに変わります。プレイヤーの両グローブが頭の高さならボディ系、下がっていれば頭系、片側が開いていればその側へのフックを重く見ます。

- **知覚**: プレイヤーのグローブ位置・速度を毎フレーム観測しますが、判断には反応遅延（強さ0で0.30秒、1で0.15秒）だけ古い姿勢を使います。相手の頭・胴へ1.2m/s以上で向かうグローブは0.15秒先へ外挿し、頭／胴・ストレート／フック・来る側を推定します。読んだ打撃に防御するかは確率（強さ0で35%、1で85%）で、完全には防ぎません。
- **適応**: 被弾は頭左・頭右・頭正面・胴の部位ごとに記憶し（半減期6秒）、次のガードや防御の選択をその部位へ偏らせます。2.5秒に3発被弾すると、予備動作を0.6倍に縮めたカウンターを割り込ませます。1.5秒に3発以上打ち込まれたときはカウンターか後退を選びます。ブロック・回避に成功した直後は高い確率で即座に反撃します。
- **防御**: ガードは頭正面（顔の前で両拳を閉じる）・頭横（こめかみ横に拳を上げ前腕で側頭を覆う）・胴正面（みぞおちの前で拳を締める）・胴横（肘を脇腹へ落とす）の4種類です。頭への打撃にはガードに加えてスリップ（頭を左右へ0.10〜0.14m）・ダッキング（0.12〜0.16m下げる）・スウェー（0.08〜0.10m後ろへ）を組み合わせることがあり、いずれも判定位置そのものを動かします。予備動作中に打撃を読むと攻撃を取りやめて防御に移ります。打撃中は取りやめません。
- **動き**: 拳・頭・足元は目標位置を臨界減衰ばね（加速度上限つき）で追従し、打撃は予備動作の終点から最小ジャーク曲線で狙いへ伸ばします。フックは横へ膨らむ1本の曲線です。ガードに当たった拳は接触点で止まり、ばねで構えへ戻ります。攻撃中も反対の拳は顎の前に残します。肘の向きと上体のひねりも時間をかけて切り替えます。
- **調整**: `BoxingTuning` の Opponent AI 欄で強さ（`opponentSkill`、既定0.6）、反応遅延・読み成功率の両端、記憶の半減期、連打の判定、ばねの時定数と加速度上限を変更できます。`enemyInterval` は判断の間隔、`telegraphSeconds` は予備動作の基準時間（ジャブは短く、フックは長い）です。テキストでの攻撃予告や対処ヒントは表示しません。難易度・緊張感の実機調整は Quest で確認してください。

手追跡が外れた際は、グローブ表示のみ最長120ms保持し、直前速度に基づく移動は最大5cmで打ち切ります。これを攻撃の有効入力にはせず、対戦・強打準備は追跡喪失として停止／リセットします。背中側の動きを推測して強打にする機能ではありません。

相手は攻撃を引き戻した後・被弾の後・待機の選択でガードを上げます。種類はプレイヤーの構え（手の平均高さと横への開き）から推定した候補を重く、被弾の多い部位をさらに重く見て確率的に選び、ガード開始時に固定します。覆っていない高さ・方向には隙が残ります。

決着したフレームで終了ゴングと勝敗音声を同時に開始します。専用音源を使い、最後の打撃音・触覚は中断しません。Demo Sessionでは終了から1秒（`BoxingTuning.resultPanelDelaySeconds`）で完了パネルを出し、ゴングと音声はそのまま最後まで鳴ります。完了・ポーズのパネル表示中はグローブの代わりにDemo Hubと同じ手（`DemoHands`）を表示し、指先で押します。Demo Session外ではゴングと音声が終わってから次のラウンドを始めます。

手・HMDのトラッキングが一瞬外れても試合は続きます。外れた手は非表示になり、その間はパンチもガードもしません。戻った手は今の位置から再開し、外れていた間の移動をパンチとして扱いません。`BoxingTuning.trackingGraceSeconds`（既定3秒）を超えて外れたままなら、`HAND TRACKING LOST` を表示して停止し、戻ると再開します。その間に新しい攻撃やダメージは発生しません。時間切れも残りHPで判定し、同じHPなら引き分けです。開始カウント「3・2・1」と勝敗「You win / You lose / It's a tie」はKenney Voiceover Packの男性音声です（CC0、`Assets/Boxing/Audio/Voice/License.txt`）。チュートリアルの日本語音声はローカルのAivisSpeech Engineで生成した「まお」の声で、音声モデルは[Aivis Common Model License 1.0](https://github.com/Aivis-Project/ACML/blob/master/ACML-1.0.md)です（商用利用可、クレジット表記は任意）。ラウンド中のBGMはUmplix「[Boxing Ring](https://opengameart.org/content/boxing-ring-0)」の曲に、同じページの観客の歓声（`boxing_match_audience.wav`）を重ねたものです（どちらもUmplix、OpenGameArt、CC0、`Assets/Boxing/Audio/Music/License.txt`）。開始ゴングで始まり、終了ゴング・再スタート・チュートリアル開始で約1秒かけて消えます。メニュー・共通ポーズ・追跡停止の間はラウンドの時計と一緒に止まり、同じ位置から再開します。チュートリアルとカウントダウンの間は流しません。音量は `BoxingTuning.musicVolume`（既定0.09、約-21dB）で、打撃音より十分小さく、ナレーション音量とは独立です。曲は55秒で、既定のラウンド（`roundSeconds` 55秒）と同じ長さです。Demo Sessionの `round` は30秒と55秒（既定）で、30秒のラウンドでは終了ゴングで曲を消します。音声は全デモ共通の-20 LUFSに正規化してあり（`hapbeat-demos/tools/tts/loudness.py`）、Demo Hubのナレーション音量（`DemoNarration.Gain`）で鳴ります。SOUND OFFは全音声に適用されます。

頭への命中は上体と頭を反らし、胴への命中は少し前屈して肘を寄せます。ガードはこの被弾リアクションを起こしません。メニュー／停止中はリアクションの時間も止まり、ラウンド再開時にリセットします。FBXに同梱した見本クリップは実行せず、ゲームの攻撃・ガードに同期した逆運動学（IK）で動かします。従来の攻撃距離が骨格の腕長を超える場合は腕を伸長して拳を判定位置に合わせます。実機での伸び方・遮蔽の見え方は調整対象です。

`Hapbeat Boxing > Install Blender Opponent` は相手モデルを再構築する操作です。モデル再出力後の骨姿勢・中心位置も更新します。通常は保存済みのBoxingシーンをそのまま開いてください。`./tools/run-unity.ps1 -Task AvatarPreview` は無音の姿勢画像を生成します。

## Hapbeat

既定の送信先はGloveBallと同じ `*/pos_l_wrist`、`*/pos_r_wrist`、`*/pos_neck` です。デバイスのposition設定を合わせ、PC（Air Link時）またはQuest（APK時）と同じ到達可能なLANに接続してください。1台で試す場合はEventMapのtargetをそのデバイスのpositionに合わせます。

Unity SDKのStreamClipを使うため、事前のKit転送は不要です。イベントごとに波形（`Assets/Boxing/Haptics/stream-clips/<イベント>.wav`）と効果音（`Assets/Boxing/Haptics/sounds/<音>.wav`）を持ちます。既定の波形は強のイベントが従来の`damage.wav`、弱のイベントが従来の`BodySoft.wav`（長めの75Hz）と同じ内容で、Studioで作り込む前の仮の割り当てです。`GainMultiplier` にプレイヤーは準備状態と振りの速さ、敵は接触速度から求めたゲインを渡し、EventMap側のゲインと掛け合わせます。効果音の音量も同じゲインに追従します。自動検証はSDK GameObjectをAwake前に無効化し、PC音声も実機送信も行いません。通常のEditor Playは音声・触覚ONです。

ガードの保持時間の基準は `BoxingTuning.guardSeconds` です。手前のグローブに当たった攻撃はガードされ、身体にはダメージが入りません。身体へ届いた攻撃のみHPを減らします。接触表示はワールド空間の小さなリングで、カメラ前の半透明パネルはありません。開始・終了にはCC0実録ベルを編集した4連打のゴングを専用音源で鳴らします。

## 触覚の作業（Hapbeat Studio Scene タブ）

| ゲームのイベント（cue 表の cue:variant） | 状況 | 鳴る場所 | EventMapエントリー | 既定の波形 / 音 |
|---|---|---|---|---|
| `punch_glove_soft` / `punch_glove_hard` | 自分のパンチが相手のグローブに当たる | 打った側の手首 | `left_` / `right_` + cue | BodySoft / damage、`impactSoft_medium_000` / `impactSoft_heavy_000` |
| `punch_body_soft` / `punch_body_hard` | 自分のパンチが相手の頭・胴・腕（グローブ以外）に当たる | 打った側の手首 | `left_` / `right_` + cue | BodySoft / damage、`impactPunch_medium_000` / `impactPunch_heavy_000` |
| `guard_glove_soft` / `guard_glove_hard` | 相手のパンチを自分のグローブで受ける | 受けた側の手首 | `left_` / `right_` + cue | BodySoft / damage、`impactSoft_medium_000` / `impactSoft_heavy_000` |
| `guard_arm_soft` / `guard_arm_hard` | 相手のパンチを自分の前腕で受ける | 受けた側の手首 | `left_` / `right_` + cue | BodySoft / damage、`impactSoft_medium_000` / `impactSoft_heavy_000` |
| `hit_body_soft` / `hit_body_hard` | 相手のパンチが自分の頭・胴に当たる | 首 | cue（`*/pos_neck`） | BodySoft / damage、`impactPunch_medium_000` / `impactPunch_heavy_000` |

- 弱／強は接触の相対速度2.5m/sが境です（自分のパンチで準備なしの当たりは常に弱）。得点・ダメージ・`EnemyBlocks` の数え方はイベントの分け方と関係なく従来のままです。
- cue 表 `Assets/Boxing/Haptics/boxing-cues.json`（hapbeat-contracts の `specs/cue-table.md`、`schema_version` 1。`hapbeat-helper cues validate` で検査）が10イベントの正本です。cue は当たった部位の5つ（`punch_glove` `punch_body` `guard_glove` `guard_arm` `hit_body`）で、弱／強はその variant `soft` / `hard` です（DEC-091。ゲームのイベント `punch_glove_soft` = cue 表の `punch_glove:soft`）。音（`sfx`）と触覚（`haptics`、手首は `at: hand` で gain 2.0、首は `at: pos_neck` で gain 0.8。手首はゲーム内のゲイン（速さ・パンチの強度で 0.12〜1）が掛かると Studio の試聴より弱くなるため高めにしている）は、親の cue に書けば両方の variant で共有し、分けるときは親に書かずに各 variant に書きます（親と variant の両方に書くと取り込みでエラー）。今は `punch_body` と `guard_glove` の音が共有で、ほかは分けています。波形は `stream-clips/<clip>.wav`、音は `sounds/<音>.wav` です（既定の音は Kenney Impact Sounds の OGG を WAV にしたもの）。
- ① `Scripts/record-haptic-clips.ps1 [-Ffmpeg <ffmpeg.exe>]`（Unity Editor を閉じて実行）: `BoxingHapticRecord` が10イベント（5 cue × 弱／強）を3回ずつPC上で再現し、プレイヤーの目線から固定30fpsで録画します（音なし・実機送信なし）。自分のパンチは台本の軌道（弱 ≈ 1m/s、強 ≈ 4m/s、引き戻しで準備）で相手のグローブ／頭・胴へ、相手の打撃は録画用の呼び口（`BoxingOpponent.ScriptedStrike`、AIは止める）で弱 ≈ 1.2m/s、強 ≈ 3.5〜4.5m/s です。ゲーム自身の判定で記録したイベント（cue 表の名前 `punch_glove:soft` など）・相対速度・ゲインをログ（`Saved/Logs/HapticRecord.log`）に残し、意図と違うイベントになった回があれば失敗します。続けて `Scripts/haptic_session.py` が記録の session（hapbeat-contracts の `specs/scene-recording.md`）を書き、`hapbeat-helper scene build` で `Saved/HapticViewer/`（git管理外）へ通し映像と Studio が読むデータを書きます。ffmpeg（libx264）、Python 3.8+、hapbeat-helper（PATH 上、または環境変数 `HAPBEAT_HELPER`）が必要です。
- ② Studio の Scene タブ（`http://localhost:5173/?tab=scene`、`repos-tools/hapbeat-studio` で `npm run dev`）を Chrome / Edge で開き、このプロジェクトのフォルダーを選んで、イベントごとに波形と音を決めて保存します。
- ③ `Scripts/import-haptic-kit.ps1`（Unity Editor を閉じて実行）: `build_haptic_import.py` が cue 表を検査して各 variant の引き継ぎを展開し（親の `sfx` / `haptics` / `variation` を、variant が書いた項目だけ置き換える。`sfxVolume` / `hapticsGain` は引き継いだ側に掛ける。route 1本、`clip` または `clips`。`variation.pick` 以外の variation / `rampTo` 等 / levelMap / ループは非対応でエラー）、`BoxingHapticImport` が波形（`clip` / `clips`）・gain・intensity を手首イベントの左右のエントリー（首は1組）へ、そのトリガーと音（`sfx.sound` または `sfx.sounds`）・音ごとの音量（`sfx.volume` × 音の intensity）・選び方を `BoxingFeedback` へ書き込みます。波形や音が複数のイベントは当たるたびに1つずつ選び、`variation.pick` が `random`（既定）なら直前と違うものをランダムに、`roundRobin` なら順番に鳴らします（波形の直前は左右で共通）。展開の検査は `python Scripts/test_build_haptic_import.py`。差分だけを書くので、変更のない cue 表を取り込んでもファイルは変わりません。取込後は APK を再ビルド・再インストールしてください。
- 作業ツールのランチャーには `boxing-vr` として登録しています（`repos-tools/hapbeat-haptic-authoring/projects/boxing-vr.json`）。
- EventMap とシーンのトリガーを既定状態から作り直すときは `./tools/run-unity.ps1 -Task HapticEvents`（`Hapbeat Boxing > Rebuild Haptic Events`）。既定値（波形1つ、gain 0.8、intensity 1、上の表の音）で18エントリーを作ります。続けて ③ を実行すると cue 表の内容になります。

## Demo Switch

logical IDは `boxing`、Android packageは `com.hapbeat.boxing`、Activityは `com.unity3d.player.UnityPlayerGameActivity`。既存のDemo Switch packageを使い、GloveBall (`jp.hapbeat.gloveballdemo`) とHand Demoへの切替先を登録しています。

MCUのCに `boxing` を設定し、切替元APKのallowlistにもboxingを追加して再ビルドする必要があります。MCUの設定だけでは未インストールAPKやUnity Editorを起動できません。既存APKと同じ認証設定を使い、共有secretをリポジトリへ保存しないでください。

## 検証・ビルド

### Wide Motion Mode（Quest単体のみ）

Androidビルドでは `Boxing Wide Motion (visual only)` OpenXR Featureを有効にし、Body Trackingを任意機能として宣言します。対応ランタイムでは実行時にBody Tracking権限を要求します。拒否・非対応・初期化失敗時も通常のXR Handsとコントローラー入力は維持します。Editor／Air LinkではWMMを起動しません。

Metaの `XR_META_hand_tracking_wide_motion_mode` を使う補助トラッカーを左右に作成します。通常のXR Handsが取得できる間はその実測姿勢が優先され、失われた場合だけWMMの手首姿勢を表示に使います。WMM1は推定の出所を区別できないため、補助トラッカーの値はすべて表示専用とし、120ms予測を重ねません。視野外の正確なパンチや強さを再現するものではありません。

WMM表示中も実測追跡の喪失時はラウンドを一時停止し、パンチ履歴を破棄します（`WMM ESTIMATED - COMBAT PAUSED`）。再認識時は既存の安定待ちを通し、位置飛びによる命中を防ぎます。視野外でもラウンドを進める仕様にはしていません。

参照: [Meta WMM仕様](https://developers.meta.com/horizon/documentation/native/android/native-wide-motion-mode/)、[独立した通常／WMMトラッカーの公式サンプル](https://github.com/meta-quest/Meta-OpenXR-SDK/blob/main/Samples/XrSamples/XrHandTrackingWideMotionMode/Src/xr_hand_helper.h)。APKのビルド成功と実機でのWMM動作確認は別です。

### コマンド

Unity Editorを閉じて、PowerShellから実行します。Unityの場所が異なる場合は `-UnityExe` を指定します。

```powershell
./tools/run-unity.ps1 -Task Tests
./tools/run-unity.ps1 -Task HapticEvents
./tools/run-unity.ps1 -Task Simulator
./tools/run-unity.ps1 -Task InputTests
./tools/run-unity.ps1 -Task SimulatorSmoke
./tools/run-unity.ps1 -Task Smoke
./tools/run-unity.ps1 -Task Capture
./tools/run-unity.ps1 -Task Windows
./tools/run-unity.ps1 -Task Android
./tools/verify-apk.ps1
```

`Logs/` にテスト結果・実行ログ・画像が出ます。Smokeは実際のPlay Modeで1ラウンド（`roundSeconds`、既定55秒）、パンチ、ガード、回避、被弾、メニュー停止を再生し、3部位・弱打／強打・接触のイベント（自分のパンチのグローブ／それ以外、ガード、被弾）・ゲイン変化・触覚送信ゼロ・エラーゼロを検証します。`Builds/Windows/HapbeatBoxing.exe`、`Builds/HapbeatBoxing.apk` が生成先です。

InputTestsはUnity公式InputTestFixtureを使うPlay Modeの入力テストです。XRIシミュレーターに加え、OpenXRのOculus Touch／Touch Plusレイアウトを検査します。実シーンで明示的なControllersモードの維持、左右スティック選択、Aで開始、グローブ追従、カウントダウン進行を確認します。テスト用XR Handsプロバイダーで、コントローラーなし起動、遅れて届く手追跡、視線で開始、追跡復帰、左手だけでメニューを開く操作も検査します。Hand Interactionデバイスと遅れて認識したTouchの姿勢が混ざらない回帰テストを含みます。InputTestsはEditor InputをSimulatorにしてネイティブXRを無効にした状態で実行し、実機確認時はAir Linkへ戻してください。SimulatorSmokeは標準Prefabへキーボードイベントを渡し、頭・左右の個別移動、ボタン、追跡喪失／復帰を実際のゲーム入力で確認します。全自動検証は無音・実機送信なしです。ビルドでは詳細レポートからEditorシミュレーター資産の混入も検査します。

各コマンドは終了コードだけでなく起動・インポートログも検査します。Unityは設定ファイルの構文エラー後もテストやScene検証を続ける場合があるため、`BOXING_SCENE_VALID` 単独では成功と扱いません。任意の起動ログは `./tools/assert-unity-log.ps1 -LogPath ./Logs/AirLinkEditor.log` で確認できます。Unityのシリアライズ済みファイルを一括で末尾空白除去しないでください。空のレイヤー名は明示的な空文字列として保持します。

3本のAPKを `Builds/DemoSwitch/` に用意した場合は、USBデバッグを許可したQuestに `./tools/install-demo-switch.ps1` で一括更新できます。既存アプリのデータは消去せず、署名が異なる場合も自動アンインストールしません。インストール後にQuestでいずれかのデモを起動してからMCUを操作します。

`Create Initial Scene` は初回だけ雛形を作成します。生成後のScene・Material・EventMapが正本で、既存Sceneを作り直しません。

## ソースとライセンス

XR設定は既存Hapbeat VR Templateを基にしています。SDKとDemo Switchは `Packages/manifest.json` のGit URL（タグ／コミット固定）から取得します。モデルと触覚は自作、打撃音とゴングはCC0素材です。[素材情報](THIRD_PARTY_NOTICES.md)を参照してください。
