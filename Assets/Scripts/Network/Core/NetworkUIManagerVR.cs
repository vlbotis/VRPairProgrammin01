using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Threading.Tasks;

public class NetworkUIManagerVR : MonoBehaviour
{
    #region Variables
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI connectionInfoText;
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private GameObject connectionPanel;

    private NetworkManagerVR networkManagerVR;
    private bool isConnecting = false;

    private enum UIState
    {
        Disconnected,
        Connecting,
        Connected
    }
    #endregion

    #region Unity Lifecycle Methods
    private void Awake()
    {
        Debug.Log($"NetworkUIManagerVR starting on GameObject: {gameObject.name}");

        // Make sure this script is on ConnectionPanel
        if (!gameObject.name.Equals("ConnectionPanel"))
        {
            Debug.LogError($"NetworkUIManagerVR must be on ConnectionPanel GameObject! Current: {gameObject.name}");
            return;
        }

        FindUIReferences();
        FindNetworkManager();
        ValidateRequiredComponents();
    }

    private void Start()
    {
        InitializeUI();
        UpdateUIState(UIState.Disconnected);
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }
    #endregion

    #region Initialization
    private void FindUIReferences()
    {
        // since this script is on ConnectionPanel, we don't need to find it
        connectionPanel = gameObject;

        // find buttons - these are direct children of this GameObject (ConnectionPanel)
        hostButton = transform.Find("HostButton")?.GetComponent<Button>();
        clientButton = transform.Find("ClientButton")?.GetComponent<Button>();
        disconnectButton = transform.Find("DisconnectButton")?.GetComponent<Button>();

        // find text elements - also direct children
        statusText = transform.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        connectionInfoText = transform.Find("ConnectionInfoText")?.GetComponent<TextMeshProUGUI>();

        // Find input field - direct child
        ipInputField = transform.Find("IPInputField")?.GetComponent<TMP_InputField>();

        // Debug info
        Debug.Log($"Finding UI References on {gameObject.name}:");
        Debug.Log($"Host Button found: {hostButton != null}");
        Debug.Log($"Client Button found: {clientButton != null}");
        Debug.Log($"Disconnect Button found: {disconnectButton != null}");
        Debug.Log($"Status Text found: {statusText != null}");
        Debug.Log($"Connection Info Text found: {connectionInfoText != null}");
        Debug.Log($"IP Input Field found: {ipInputField != null}");
    }

    private void FindNetworkManager()
    {
        networkManagerVR = FindFirstObjectByType<NetworkManagerVR>();
        if (networkManagerVR == null)
        {
            Debug.LogError("NetworkManagerVR not found in scene!");
        }
    }

    private void ValidateRequiredComponents()
    {
        if (hostButton == null) Debug.LogError("Host button not found!");
        if (clientButton == null) Debug.LogError("Client button not found!");
        if (disconnectButton == null) Debug.LogError("Disconnect button not found!");
        if (statusText == null) Debug.LogError("Status text not found!");
        if (connectionInfoText == null) Debug.LogError("Connection info text not found!");
    }

    private void InitializeUI()
    {
        SetupButtonListeners();
        SubscribeToNetworkEvents();
        InitializeUIText();
    }

    private void SetupButtonListeners()
    {
        if (hostButton != null)
        {
            hostButton.onClick.AddListener(HandleHostButtonClick);
        }

        if (clientButton != null)
        {
            clientButton.onClick.AddListener(HandleClientButtonClick);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.AddListener(HandleDisconnectButtonClick);
        }
    }

    private void SubscribeToNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton is null!");
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
    }

    private void InitializeUIText()
    {
        if (statusText != null)
        {
            statusText.text = "Disconnected";
        }

        if (connectionInfoText != null)
        {
            connectionInfoText.text = "Connection Info: None";
        }
    }
    #endregion

    #region Button Handlers
    private async void HandleHostButtonClick()
    {
        await StartHost();
    }

    private async void HandleClientButtonClick()
    {
        await StartClient();
    }

    private void HandleDisconnectButtonClick()
    {
        Disconnect();
    }

    private async Task StartHost()
    {
        if (isConnecting) return;

        try
        {
            isConnecting = true;
            UpdateUIState(UIState.Connecting);
            statusText.text = "Starting Host...";

            bool success = await networkManagerVR.StartHost();

            if (success)
            {
                connectionInfoText.text = $"Connection Info: {networkManagerVR.GetConnectionInfo()}";
                UpdateUIState(UIState.Connected);
                statusText.text = "Hosting";
            }
            else
            {
                UpdateUIState(UIState.Disconnected);
                statusText.text = "Failed to start host";
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error starting host: {e}");
            statusText.text = "Error starting host";
            UpdateUIState(UIState.Disconnected);
        }
        finally
        {
            isConnecting = false;
        }
    }

    private async Task StartClient()
    {
        if (isConnecting) return;

        try
        {
            isConnecting = true;
            UpdateUIState(UIState.Connecting);
            statusText.text = "Connecting as Client...";

            bool success = await networkManagerVR.StartClient();

            if (!success)
            {
                UpdateUIState(UIState.Disconnected);
                statusText.text = "Failed to connect";
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error connecting as client: {e}");
            statusText.text = "Error connecting as client";
            UpdateUIState(UIState.Disconnected);
        }
        finally
        {
            isConnecting = false;
        }
    }

    private void Disconnect()
    {
        networkManagerVR.Disconnect();
        UpdateUIState(UIState.Disconnected);
        statusText.text = "Disconnected";
    }
    #endregion

    #region Network Callbacks
    private void OnClientConnected(ulong clientId)
    {
        try
        {
            // First check if NetworkManager.Singleton exists
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("NetworkManager.Singleton is null in OnClientConnected!");
                return;
            }

            string connectionStatus = "";

            // Set appropriate status message
            if (NetworkManager.Singleton.IsHost)
            {
                connectionStatus = $"Hosting - Client {clientId} Connected";
            }
            else
            {
                connectionStatus = "Connected to Host";
            }

            // Safely update UI
            if (statusText != null)
            {
                statusText.text = connectionStatus;
            }
            else
            {
                Debug.LogError("StatusText is null in OnClientConnected!");
            }

            // Safely update UI state
            UpdateUIState(UIState.Connected);

            Debug.Log($"[NetworkUI] OnClientConnected: {connectionStatus}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in OnClientConnected: {e.Message}");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsClient)
        {
            UpdateUIState(UIState.Disconnected);
            statusText.text = "Disconnected";
        }
    }

    private void OnTransportFailure()
    {
        UpdateUIState(UIState.Disconnected);
        statusText.text = "Connection Failed";
    }
    #endregion

    #region UI State Management
    private void UpdateUIState(UIState state)
    {
        Debug.Log($"[NetworkUI] Updating UI State to: {state}");

        try
        {
            // First verify all UI components exist
            if (hostButton == null)
            {
                Debug.LogError("Host button is null in UpdateUIState!");
                return;
            }
            if (clientButton == null)
            {
                Debug.LogError("Client button is null in UpdateUIState!");
                return;
            }
            if (disconnectButton == null)
            {
                Debug.LogError("Disconnect button is null in UpdateUIState!");
                return;
            }
            if (connectionPanel == null)
            {
                Debug.LogError("Connection panel is null in UpdateUIState!");
                return;
            }

            switch (state)
            {
                case UIState.Disconnected:
                    hostButton.interactable = true;
                    clientButton.interactable = true;
                    disconnectButton.interactable = false;
                    connectionPanel.SetActive(true);
                    break;

                case UIState.Connecting:
                    hostButton.interactable = false;
                    clientButton.interactable = false;
                    disconnectButton.interactable = false;
                    connectionPanel.SetActive(true);
                    break;

                case UIState.Connected:
                    hostButton.interactable = false;
                    clientButton.interactable = false;
                    disconnectButton.interactable = true;
                    connectionPanel.SetActive(true);
                    break;
            }

            // Log successful state update
            Debug.Log($"[NetworkUI] Successfully updated UI to state: {state}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in UpdateUIState: {e.Message}");
        }
    }
    #endregion
}
    //private void SetupUICallbacks()
    //{
    //    hostButton.onClick.AddListener(() =>
    //    {
    //        hostButton.interactable = false;
    //        clientButton.interactable = false;
    //        statusText.text = "Starting Host...";
    //        networkManagerVR.StartHost();
    //    });

    //    clientButton.onClick.AddListener(() =>
    //    {
    //        hostButton.interactable = false;
    //        clientButton.interactable = false;
    //        statusText.text = "Connecting as Client...";
    //        networkManagerVR.StartClient();
    //    });

    //    NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
    //    {
    //        if (NetworkManager.Singleton.IsHost)
    //            statusText.text = $"Hosting - Client {id} Connected";
    //        else
    //            statusText.text = "Connected to Host";
    //    };

    //    NetworkManager.Singleton.OnClientDisconnectCallback += (id) =>
    //    {
    //        statusText.text = "Disconnected";
    //        hostButton.interactable = true;
    //        clientButton.interactable = true;
    //    };
    //} 

