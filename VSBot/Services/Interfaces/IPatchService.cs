namespace VSBot.Services.Interfaces
{
    public interface IPatchService
    {
        /// <summary>
        /// Applies all patches whose type is <c>AutoFix</c>.
        /// Safe to call multiple times; already-applied patches are skipped.
        /// Intended to be called once during plugin load.
        /// </summary>
        void ApplyAllAutoPatches();

        /// <summary>
        /// Applies a single patch by its registered name.
        /// Has no effect if the patch is already applied.
        /// </summary>
        /// <param name="patchName">The key identifying the patch definition.</param>
        /// <returns><c>true</c> if the patch was applied or was already active; otherwise, <c>false</c>.</returns>
        bool ApplyPatch(string patchName);

        /// <summary>
        /// Restores the original bytes for a named patch, effectively disabling it.
        /// Has no effect if the patch is not currently applied.
        /// </summary>
        /// <param name="patchName">The key identifying the patch to restore.</param>
        void RestorePatch(string patchName);

        /// <summary>
        /// Restores all currently applied patches to their original byte state.
        /// This method is also called automatically when the service is disposed.
        /// </summary>
        void RestoreAll();
    }
}
