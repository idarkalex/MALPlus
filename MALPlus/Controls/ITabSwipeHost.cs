namespace MALPlus.Controls;

public interface ITabSwipeHost
{
    bool TrySwipe(int direction);
}

public interface ISwipePreviewHost : ITabSwipeHost
{
    bool BeginSwipePreview(int direction);
    void UpdateSwipePreview(double dxDips);
    void EndSwipePreview(double dxDips, double velocityDipsPerSecond);
}
