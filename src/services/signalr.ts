import * as signalR from '@microsoft/signalr';
import { API_BASE_URL, AuthTokenStore } from './api';

export class ChatSignalRService {
  private static connection: signalR.HubConnection | null = null;
  private static messageCallbacks: Set<(msg: any) => void> = new Set();
  private static statusCallbacks: Set<(isConnected: boolean) => void> = new Set();

  /**
   * Get the primary .NET Core SignalR Chat Hub URL
   */
  public static getHubUrl(): string {
    const baseUrl = API_BASE_URL;
    return `${baseUrl}/hubs/chat`;
  }

  /**
   * Initialize or return the existing SignalR Hub Connection
   */
  public static async getConnection(): Promise<signalR.HubConnection> {
    if (this.connection && (this.connection.state === signalR.HubConnectionState.Connected || this.connection.state === signalR.HubConnectionState.Connecting)) {
      return this.connection;
    }

    const hubUrl = this.getHubUrl();

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => AuthTokenStore.getAccessToken() || '',
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Bind incoming message events from .NET Core SignalR ChatHub
    const handleIncomingMessage = (message: any) => {
      this.messageCallbacks.forEach((cb) => {
        try {
          cb(message);
        } catch (err) {
          console.error('Error in SignalR message callback:', err);
        }
      });
    };

    this.connection.on('ReceiveMessage', handleIncomingMessage);
    this.connection.on('ReceiveChatMessage', handleIncomingMessage);
    this.connection.on('MessageReceived', handleIncomingMessage);
    this.connection.on('NewMessage', handleIncomingMessage);
    this.connection.on('UserConnected', (user) => console.log('SignalR user connected:', user));
    this.connection.on('UserDisconnected', (user) => console.log('SignalR user disconnected:', user));

    this.connection.onreconnecting(() => {
      this.notifyStatus(false);
    });

    this.connection.onreconnected(() => {
      this.notifyStatus(true);
    });

    this.connection.onclose(() => {
      this.notifyStatus(false);
    });

    return this.connection;
  }

  private static notifyStatus(isConnected: boolean) {
    this.statusCallbacks.forEach((cb) => {
      try {
        cb(isConnected);
      } catch (err) {
        console.error('Error in SignalR status callback:', err);
      }
    });
  }

  /**
   * Start the SignalR connection and register listener callbacks
   */
  public static async startConnection(
    onMessageReceived?: (msg: any) => void,
    onStatusChange?: (isConnected: boolean) => void
  ): Promise<signalR.HubConnection | null> {
    if (onMessageReceived) {
      this.messageCallbacks.add(onMessageReceived);
    }
    if (onStatusChange) {
      this.statusCallbacks.add(onStatusChange);
    }

    try {
      const conn = await this.getConnection();

      if (conn.state === signalR.HubConnectionState.Disconnected) {
        await conn.start();
        this.notifyStatus(true);
      } else if (conn.state === signalR.HubConnectionState.Connected) {
        this.notifyStatus(true);
      }

      return conn;
    } catch (err) {
      console.warn('Real .NET Core SignalR connection attempt to', this.getHubUrl(), 'failed:', err);
      this.notifyStatus(false);
      return null;
    }
  }

  /**
   * Remove listener callbacks
   */
  public static removeListeners(
    onMessageReceived?: (msg: any) => void,
    onStatusChange?: (isConnected: boolean) => void
  ) {
    if (onMessageReceived) {
      this.messageCallbacks.delete(onMessageReceived);
    }
    if (onStatusChange) {
      this.statusCallbacks.delete(onStatusChange);
    }
  }

  /**
   * Stop the SignalR connection
   */
  public static async stopConnection() {
    if (this.connection) {
      try {
        await this.connection.stop();
      } catch (err) {
        console.error('Error stopping SignalR:', err);
      }
      this.connection = null;
    }
    this.notifyStatus(false);
  }

  /**
   * Send message via .NET Core SignalR Hub method (if available)
   */
  public static async sendMessage(conversationId: string, content: string): Promise<boolean> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      return false;
    }
    try {
      await this.connection.invoke('SendMessage', conversationId, content);
      return true;
    } catch (err) {
      console.warn('SignalR Hub method SendMessage failed, fallback to REST API:', err);
      return false;
    }
  }
}
