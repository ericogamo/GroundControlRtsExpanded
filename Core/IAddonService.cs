namespace GroundControlRtsExpanded;

internal interface IAddonTick
{
    void Tick();
}

internal interface IAddonResetSession
{
    void ResetSession();
}

internal interface IAddonGui
{
    void DrawGui();
}
