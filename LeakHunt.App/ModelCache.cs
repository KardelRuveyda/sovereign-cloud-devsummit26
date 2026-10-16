// One model cache for the app and the benchmark, so both load the same model files.
// Override with the LH_MODEL_CACHE environment variable.
static class ModelCache
{
    public static string Dir =>
        Environment.GetEnvironmentVariable("LH_MODEL_CACHE")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".foundry", "cache", "models");
}
