/// <summary>
/// フェードイン前に初期化完了を待つ必要があるシーンコンポーネントが実装します。
/// </summary>
public interface ISceneLoadReady
{
    bool IsSceneLoadReady { get; }
    string SceneLoadStatus { get; }
}
