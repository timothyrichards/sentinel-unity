using System;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

public class SpacetimeManager : MonoBehaviour
{
    const string SERVER_URL = "https://maincloud.spacetimedb.com";
    const string MODULE_NAME = "sentinel";

    public static SpacetimeManager Instance { get; private set; }
    public static Identity LocalIdentity { get; private set; }
    public static DbConnection Conn { get; private set; }
    public static event Action OnConnected;
    public static event Action OnSubscriptionApplied;

    private SubscriptionBuilder _subscriptionBuilder;

    private void Start()
    {
        Instance = this;
        Application.targetFrameRate = 60;

        // In order to build a connection to SpacetimeDB we need to register
        // our callbacks and specify a SpacetimeDB server URI and module name.
        var builder = DbConnection.Builder()
            .OnConnect(HandleConnect)
            .OnConnectError(HandleConnectError)
            .OnDisconnect(HandleDisconnect)
            .WithUri(SERVER_URL)
            .WithModuleName(MODULE_NAME);

        // Clear cached connection data to ensure proper connection (enable when switching servers)
        // PlayerPrefs.DeleteKey("spacetimedb.identity_token - " + Application.dataPath);

        // If the user has a SpacetimeDB auth token stored in the Unity PlayerPrefs,
        // we can use it to authenticate the connection.
        if (AuthToken.Token != "")
        {
            builder = builder.WithToken(AuthToken.Token);
        }

        // Building the connection will establish a connection to the SpacetimeDB
        // server.
        Conn = builder.Build();
    }

    // Called when we connect to SpacetimeDB and receive our client identity
    void HandleConnect(DbConnection _conn, Identity identity, string token)
    {
        Debug.Log("Connected.");
        AuthToken.SaveToken(token);
        LocalIdentity = identity;

        // Initialize the ReducerMiddleware
        ReducerMiddleware.Instance.Initialize(Conn.Reducers);

        // Request relevant tables
        _subscriptionBuilder = Conn.SubscriptionBuilder().OnApplied(HandleSubscriptionApplied);

        OnConnected?.Invoke();
    }

    public void AddSubscription(string query)
    {
        _subscriptionBuilder.Subscribe(new string[] { query });
    }

    void HandleConnectError(Exception ex)
    {
        Debug.LogError($"Connection error: {ex}");
    }

    void HandleDisconnect(DbConnection _conn, Exception ex)
    {
        Debug.Log("Disconnected.");
        if (ex != null)
        {
            Debug.LogException(ex);
        }
    }

    private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
    {
        OnSubscriptionApplied?.Invoke();
    }

    public static bool IsConnected()
    {
        return Conn != null && Conn.IsActive;
    }

    public void Disconnect()
    {
        Conn.Disconnect();
        Conn = null;
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}