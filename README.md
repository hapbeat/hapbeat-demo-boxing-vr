# Hapbeat Boxing

Unity **6000.3.12f1** の90秒VRスパーリングデモ。左右のグローブで攻撃・ガードし、頭を動かして相手のパンチを避けます。3カウント後にラウンドが始まり、終了時に得点と再開メニューを表示します。Hapbeatが未接続でもゲームは動作します。

スコアボードには自分と相手のHPバーを表示します。初期HPは各100、プレイヤーの攻撃は準備状態とHMD相対速度に応じて0.5〜20、敵の通常ヒットは10・強ヒットは20ダメージ、グローブ・腕でのガードはダメージなしです。HPが0になるとKOで終了し、再スタート時に全回復します。ガード成立時は相手のパンチを接触点で止め、そこから引き戻します。攻撃のターン制限はありません。

## Editor + XR Interaction Simulator（HMD不要）

1. `Hapbeat Boxing > Editor Input > Simulator` を選び、Playを押します。Gameビューをクリックしてキーボード入力を渡してください。
2. ゲームの入力は仮想 `Controllers` を使います。キーボードで拳を直接動かすDesktopモードはありません。
3. `Hapbeat Boxing > Editor Input > Controls` に操作ガイドがあります。TabでFPS／デバイス操作を切替、Hで頭、`[`／`]`で左右デバイスを選択し、WASDで移動、Q/Eで上下移動、右マウスドラッグ／矢印キーで回転、Rでリセットします。仮想コントローラーの1がA/X、2がB/Yです。Escapeでゲームメニュー、Enterで決定もできます。

Unity XRI 3.3.1の標準シミュレーターPrefab・入力設定を使用します。独自のテスト姿勢をゲームへ直接渡す機能ではなく、実機と同じInput System経由で頭・両手・ボタンを読みます。操作ガイドはEditorウィンドウに表示し、サンプルの装飾UIは取り込んでいません。

シミュレーター選択時はEditorのネイティブOpenXR起動を無効にします。Air Linkへ戻す時はPlayを止め、`Hapbeat Boxing > Editor Input > Air Link` を選んでください。選択はこのPC・このプロジェクト専用です。AndroidのOpenXR設定は変えず、シミュレーター資産は `Assets/Boxing/Editor/Simulator` に隔離してAPKへ含めません。

## Editor + Quest Air Link

1. この `boxing-vr` フォルダーをUnity 6000.3.12f1で開き、`Assets/Boxing/Scenes/Boxing.unity` を開きます。
2. Meta Horizon LinkでQuestをAir Link接続します。WindowsのOpenXR runtimeはMeta Horizon Linkを選択してください。
3. `Hapbeat Boxing > Editor Input > Air Link` を選び、Unityのactive build targetをWindowsにしてPlayを押します。起動時メニューの `START 90s ROUND` を選択します。

手追跡（Hands）が既定です。メニューでは半透明の手を表示し、親指と人差し指の中点から伸びるレイで項目を指し、つまんで決定します。Meta Aimの照準方向・ピンチ入力を使用し、対応デバイスが存在しない環境では実測の手首方向と指先間隔を使います。Meta Aimが存在するが無効／システムジェスチャー中の場合は選択を止めます。視線だけでは選択されません。コントローラーは不要です。起動時のHMD位置・向きをリング上の開始位置に合わせます。必要ならメニューから `RECENTER` を選択します。

レイの起点はXRI Hands Interaction DemoのPinchPointFollowと同じ中点方式です。メニュー項目の当たり判定はデモ独自実装を維持しています。手の表示はUnity XR Handsの標準モデル・XRHandSkeletonDriverを利用し、試合中はグローブへ戻ります。標準手モデルはCC0ではなく、同梱の `Assets/Boxing/Art/UnityHands/LICENSE.md` が適用されます。

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

NPCは左右の上腕・前腕にも、描画リグに追従する連続スイープ判定を持ちます。グローブ・腕・身体のうち最初に接触した面を採用し、腕で遮られたパンチは身体を傷つけません。腕への接触は既存のグローブ接触と同じ音・触覚分類です。NPCグローブは幅18cm、プレイヤーは既存の15cmを維持します。

プレイヤーにはグローブの手首方向へ伸びる長さ28cm・直径9cmの簡易前腕を表示します。実際の肘の推定ではなく、手首の向きに固定した防御用モデルです。前腕への接触も同じ左腕／右腕のガードとして処理し、対応する手首の音・触覚ルートを使います。前腕自体は相手にダメージを与えません。判定は既存の移動掃引方式（重なる13個の球）で、頭・胴・グローブと最初の接触を比較します。追跡喪失・メニュー中には判定せず、メニューでは前腕も非表示です。

### 外部操作への接続点とHands

`BoxingGame.RecenterPlayer()`、`UseHandTracking()`、`UseControllers()`、`StartRound()`が外部操作用のローカル呼び出し口です。Unityのメインスレッドで呼びます。M5スイッチャーの現行契約はデモ切替のみなので、リセンター等のネットワーク命令は**未接続**です。次の通信拡張はcontracts-firstで追加し、この呼び出し口へ接続します。

Hands処理およびStandalone/AndroidのHand Tracking Subsystemは有効です。起動後に手のサブシステムが使えるようになった場合も再取得します。`WAITING FOR HAND SUBSYSTEM`は動作中の手サブシステムがない状態、`WRIST TRACKING: LEFT ... / RIGHT ...`は各手首の取得状態です。Consoleの`[Boxing Input]`と`[Boxing State]`はソース・追跡・停止理由の変化を記録します。実機Handsの動作は自動入力テストでは保証しません。

- 開始位置・正面はシーンの `Arena - original procedural assets / Start position` で調整できます。PositionのX/ZとRotationのYを使い、既定の `(0, 0.003, 0.2)`・Y回転`0`は相手に正対します。高さはHMDの追跡値を維持します。`XR Origin (Boxing)` のBoxingInputにあるStart Pointが参照先です。Main Cameraを直接回転しても追跡値で上書きされます。初回追跡時・ラウンド開始時・RECENTER時にこの開始位置へ合わせます。
- Air Link中はPCのGameビューではなくOpenXRセッションのフォーカスを使って停止判定します。`HEADSET PAUSED - OPENXR NOT FOCUSED` はQuestのシステム画面等でVR側のフォーカスがない状態、`APPLICATION PAUSED` はアプリ中断、`GAME WINDOW NOT FOCUSED` はネイティブXRがないSimulator側のウィンドウ非アクティブを表します。実際の追跡喪失・安全範囲逸脱による停止は維持します。
- プレイヤーの判定は左右グローブと頭。プレイヤーの腕は描画・判定しません。相手には見た目用の腕・脚があります。
- 相手はジャブ、クロス、フックを繰り返します。足を前後に置き、膝の曲げと小さな上体の揺れで待機します。パンチでは肩の回転・上体の前進・拳の軌道を連動させます。狙いは予備動作開始時に固定し、パンチ中は頭を追尾しません。
- メニュー表示、Questのシステムメニュー・フォーカス喪失、追跡喪失、開始点から1m以上離れた時は対戦時間・敵・触覚を停止します。システムメニューから戻った時は `RESUME` で明示的に再開します。
- `Assets/Boxing/BoxingTuning.asset` でラウンド時間、攻撃間隔、予備動作、判定サイズ、強打の再準備・速度、敵の速度→ゲインのカーブ・閾値を変更できます。
- `IMPACT: WeakHard` はプレイヤーの距離強度0.65、敵は接触速度2.5m/sを境に波形を変更します。`Continuous` は同じ波形のゲインだけを連続変更します。接触速度0.25m/s未満は無視しますが、それ以上のプレイヤーの強度は速度ではなく距離で決まります。敵は6m/sで最大ゲインです。
- `Assets/Boxing/Haptics/BoxingEventMap.asset` の12エントリー（左右手首／頭 × グローブ接触／身体接触 × 弱／強）で波形、絶対ゲイン、送信先を編集できます。攻撃側・防御側とも接触材質で音と触覚を選びます。頭への命中も身体接触に含みます。

## 相手のリグ付きモデル

相手はBlenderで制作したローポリボクサーで、小さな鼻・耳を持ちます。`Assets/Boxing/Art/Boxer.fbx` と20本の骨を使用し、元データは `art-source/boxing-opponent/` にあります。リセンター時のHMD高さへ頭中心を合わせ、通常のしゃがみでは縮尺を変えません。`BoxingOpponentAvatar` が拳・頭の判定位置へ骨格を合わせます。手首は前腕に沿わせ、反対の拳はガード位置を維持します。

グローブは同じ元メッシュから両者へ反映し、幅15cm・厚さ約10.1cm・全長約23.8cmへ統一しています。丸い打撃面・掌側へ折れた指のパッド・内側の親指・赤い幅広カフと細い白縁を備え、相手の体格を変えても実寸は一定です。左右の親指・掌・手首の向きはメッシュ上の目印で回帰検証します。球形の接触判定は直径14cm。これはデモの調整値であり、特定製品や全競技共通の寸法を再現したものではありません。相手は目線基準の高さを保ち、従来より幅12％・厚み10％を抑えています。

相手の攻撃は頭ストレート・胴ストレート・頭フック・胴フックの4種類です。胴狙いでは構えを下げ、頭から43cm下の胴体中心を狙います。頭だけのガードでは胴体を防げません。プレイヤーのパンチ開始を待機中に観測すると時折反対の高さへカウンターを試みます（最短4秒間隔、予備動作あり、攻撃中は割込みなし）。胴体被弾の触覚は、追加デバイスを要求せず既存の首側受信先を共用します。

手追跡が外れた際は、グローブ表示のみ最長120ms保持し、直前速度に基づく移動は最大5cmで打ち切ります。これを攻撃の有効入力にはせず、対戦・強打準備は追跡喪失として停止／リセットします。背中側の動きを推測して強打にする機能ではありません。

相手のガードは頭／胴×正面ストレート／横フックの4種類です。正面では両拳を中央に寄せ、フックでは左右側面へ上げ、攻撃を引き戻した後にもガードします。構えの手の平均高さと横への開きから1種類を推定し、55％はその候補、45％は4種類からランダム選択します。ガード開始時に選択を固定し、プレイヤーの攻撃を瞬時に追尾する完全防御にはしません。覆っていない高さ・方向には隙が残ります。

決着したフレームで終了ゴングと勝敗音声を同時に開始します。専用音源を使い、最後の打撃音・触覚は中断しません。終了表示は最低2.5秒、ゴングと音声が終わるまで維持してからメニューへ進みます。その間に新しい攻撃やダメージは発生しません。時間切れも残りHPで判定し、同じHPなら引き分けです。開始カウント「3・2・1」と勝敗「You win / You lose / It's a tie」はKenney Voiceover Packの男性音声です（CC0、`Assets/Boxing/Audio/Voice/License.txt`）。SOUND OFFは全音声に適用されます。

頭への命中は上体と頭を反らし、胴への命中は少し前屈して肘を寄せます。ガードはこの被弾リアクションを起こしません。メニュー／停止中はリアクションの時間も止まり、ラウンド再開時にリセットします。FBXに同梱した見本クリップは実行せず、ゲームの攻撃・ガードに同期した逆運動学（IK）で動かします。従来の攻撃距離が骨格の腕長を超える場合は腕を伸長して拳を判定位置に合わせます。実機での伸び方・遮蔽の見え方は調整対象です。

`Hapbeat Boxing > Install Blender Opponent` は相手モデルを再構築する操作です。モデル再出力後の骨姿勢・中心位置も更新します。通常は保存済みのBoxingシーンをそのまま開いてください。`./tools/run-unity.ps1 -Task AvatarPreview` は無音の姿勢画像を生成します。

## Hapbeat

既定の送信先はGloveBallと同じ `*/pos_l_wrist`、`*/pos_r_wrist`、`*/pos_neck` です。デバイスのposition設定を合わせ、PC（Air Link時）またはQuest（APK時）と同じ到達可能なLANに接続してください。1台で試す場合はEventMapのtargetをそのデバイスのpositionに合わせます。

Unity SDKのStreamClipを使うため、事前のKit転送は不要です。グローブ同士は短い155Hzの触覚、身体への命中は長めの75Hzの触覚です。`GainMultiplier` にプレイヤーは距離応答、敵は接触速度応答を渡し、EventMap側のゲインと掛け合わせます。自動検証はSDK GameObjectをAwake前に無効化し、PC音声も実機送信も行いません。通常のEditor Playは音声・触覚ONです。

相手はパンチとは独立に、待機中にガードを上げ下げします。`BoxingTuning.guardInterval` と `guardSeconds` で間隔／保持時間を調整できます。手前のグローブに当たった攻撃はガードされ、身体にはダメージが入りません。身体へ届いた攻撃のみHPを減らします。接触表示はワールド空間の小さなリングで、カメラ前の半透明パネルはありません。開始・終了にはCC0実録ベルを編集した4連打のゴングを専用音源で鳴らします。

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
./tools/run-unity.ps1 -Task Simulator
./tools/run-unity.ps1 -Task InputTests
./tools/run-unity.ps1 -Task SimulatorSmoke
./tools/run-unity.ps1 -Task Smoke
./tools/run-unity.ps1 -Task Capture
./tools/run-unity.ps1 -Task Windows
./tools/run-unity.ps1 -Task Android
./tools/verify-apk.ps1
```

`Logs/` にテスト結果・実行ログ・画像が出ます。Smokeは実際のPlay Modeで90秒ラウンド、パンチ、ガード、回避、被弾、メニュー停止を再生し、3部位・弱打／強打・ゲイン変化・触覚送信ゼロ・エラーゼロを検証します。`Builds/Windows/HapbeatBoxing.exe`、`Builds/HapbeatBoxing.apk` が生成先です。

InputTestsはUnity公式InputTestFixtureを使うPlay Modeの入力テストです。XRIシミュレーターに加え、OpenXRのOculus Touch／Touch Plusレイアウトを検査します。実シーンで明示的なControllersモードの維持、左右スティック選択、Aで開始、グローブ追従、カウントダウン進行を確認します。テスト用XR Handsプロバイダーで、コントローラーなし起動、遅れて届く手追跡、視線で開始、追跡復帰、左手だけでメニューを開く操作も検査します。Hand Interactionデバイスと遅れて認識したTouchの姿勢が混ざらない回帰テストを含みます。InputTestsはEditor InputをSimulatorにしてネイティブXRを無効にした状態で実行し、実機確認時はAir Linkへ戻してください。SimulatorSmokeは標準Prefabへキーボードイベントを渡し、頭・左右の個別移動、ボタン、追跡喪失／復帰を実際のゲーム入力で確認します。全自動検証は無音・実機送信なしです。ビルドでは詳細レポートからEditorシミュレーター資産の混入も検査します。

各コマンドは終了コードだけでなく起動・インポートログも検査します。Unityは設定ファイルの構文エラー後もテストやScene検証を続ける場合があるため、`BOXING_SCENE_VALID` 単独では成功と扱いません。任意の起動ログは `./tools/assert-unity-log.ps1 -LogPath ./Logs/AirLinkEditor.log` で確認できます。Unityのシリアライズ済みファイルを一括で末尾空白除去しないでください。空のレイヤー名は明示的な空文字列として保持します。

3本のAPKを `Builds/DemoSwitch/` に用意した場合は、USBデバッグを許可したQuestに `./tools/install-demo-switch.ps1` で一括更新できます。既存アプリのデータは消去せず、署名が異なる場合も自動アンインストールしません。インストール後にQuestでいずれかのデモを起動してからMCUを操作します。

`Create Initial Scene` は初回だけ雛形を作成します。生成後のScene・Material・EventMapが正本で、既存Sceneを作り直しません。

## ソースとライセンス

XR設定は既存Hapbeat VR Templateを基にしています。SDKはworkspace内の `repos-sdk/hapbeat-unity-sdk` を参照します。別環境では同じworkspace構造を用意するか `Packages/manifest.json` のSDK参照を変更してください。モデルと触覚は自作、打撃音とゴングはCC0素材です。[素材情報](THIRD_PARTY_NOTICES.md)を参照してください。
