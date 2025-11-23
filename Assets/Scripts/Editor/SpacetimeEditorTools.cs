using UnityEditor;
using UnityEngine;

public static class SpacetimeEditorTools
{
    private const string IDENTITY_TOKEN_KEY = "spacetimedb.identity_token";

    [MenuItem("SpacetimeDB/Clear Player Identity")]
    public static void ClearPlayerIdentity()
    {
        string dataPathKey = $"{IDENTITY_TOKEN_KEY} - {Application.dataPath}";

        if (PlayerPrefs.HasKey(dataPathKey))
        {
            PlayerPrefs.DeleteKey(dataPathKey);
            PlayerPrefs.Save();
            Debug.Log("[SpacetimeDB] Cleared identity token. You will get a new identity on next connect.");
        }
        else
        {
            Debug.Log("[SpacetimeDB] No identity token found to clear.");
        }
    }
}
