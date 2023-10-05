// <copyright file="ConnectionManager.cs" company="MuJS">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MuJS;

using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

/// <summary>
/// Class which manages the connections which are created through the game client.
/// To identify each connection, we use handles (a simple number).
/// </summary>
public static unsafe class ConnectionManager
{
    /// <summary>
    /// The currently active connections, with their handle as key.
    /// </summary>
    private static readonly Dictionary<int, SslStream> Connections = new();

    /// <summary>
    /// The currently used maximum handle number.
    /// </summary>
    private static int _maxHandle;

    /// <summary>
    /// Connects the specified host and port.
    /// </summary>
    /// <param name="hostPtr">The pointer to a string which contains the host (ip or hostname).</param>
    /// <param name="port">The port.</param>
    /// <param name="onPacketReceived">
    /// The pointer to an unmanaged method which is called when a new packet got received.
    /// Parameters: handle, size, pointer to the data.
    /// </param>
    /// <param name="onDisconnected">
    /// The pointer to an unmanaged method which is called when the connection got disconnected.
    /// Parameter: handle.
    /// </param>
    /// <returns>
    /// The handle of the created connection. If negative, the connection couldn't be established.
    /// </returns>
    [UnmanagedCallersOnly]
    public static int Connect(IntPtr hostPtr, int port, delegate* unmanaged<int, int, byte*, void> onPacketReceived, delegate* unmanaged<int, void> onDisconnected)
    {
        try
        {
            var host = Marshal.PtrToStringAnsi(hostPtr) ?? throw new ArgumentNullException(nameof(hostPtr));

            TcpClient client = new TcpClient();
            client.Connect(host, port);

            // Create an SSL stream over the network stream
            SslStream sslStream = new(client.GetStream(), false, (sender, certificate, chain, errors) =>
            {
                return true;
            });

            // Authenticate using your SSL certificate
            X509Certificate2 certificate = new X509Certificate2("Data\\server.crt");
            sslStream.AuthenticateAsClient(host, new X509Certificate2Collection(certificate), SslProtocols.Tls12, false);

            if (sslStream.IsAuthenticated)
            {
                Console.WriteLine("SSL connection established successfully.");
            }
            else
            {
                Console.WriteLine("SSL connection could not be established.");
            }

            Console.WriteLine("Connected to the server.");
            var handle = Interlocked.Increment(ref _maxHandle);
            Connections.Add(handle, sslStream);

            // Create a thread to listen for incoming data
            Thread listenThread = new(() => ListenForData(sslStream, onPacketReceived, onDisconnected, handle));
            listenThread.Start();

            return handle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error establishing connection: {ex}");
            return -1;
        }
    }

    /// <summary>
    /// Sends a packet over the connection of the specified handle.
    /// </summary>
    /// <param name="handle">The handle of the connection.</param>
    /// <param name="data">The pointer to the packet data.</param>
    /// <param name="count">The count of bytes which should be sent.</param>
    [UnmanagedCallersOnly]
    public static void Send(int handle, byte* data, int count)
    {
        if (Connections.TryGetValue(handle, out var connection))
        {
            try
            {
                byte[] buffer = new byte[count];
                Marshal.Copy((IntPtr)data, buffer, 0, count);
                connection.Write(buffer, 0, count);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[MuJS] C->S {BytesToHex(buffer)}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending {0} bytes with handle {1}: {2}", count, handle, ex);
            }
        }
        else
        {
            Console.WriteLine("Connection with handle {0} not found.", handle);
        }
    }

    /// <summary>
    /// Disconnects the connection of the specified handle.
    /// </summary>
    /// <param name="connectionHandle">The handle of the connection.</param>
    [UnmanagedCallersOnly]
    public static void Disconnect(int connectionHandle)
    {
        if (Connections.TryGetValue(connectionHandle, out var connection))
        {
            connection.Close();
        }
    }

    /// <summary>
    /// Gets the packet size.
    /// </summary>
    /// <param name="packet">The packet bytes.</param>
    /// <returns>The packet size.</returns>
    private static int GetPacketSize(this Span<byte> packet)
    {
        return packet[0] switch
        {
            0xC1 or 0xC3 => packet[1],
            0xC2 or 0xC4 => packet[1] << 8 | packet[2],
            _ => 0,
        };
    }

    /// <summary>
    /// Thread that listens for incoming packets.
    /// </summary>
    /// <param name="stream">The connection stream.</param>
    /// <param name="onPacketReceived">Callback for onPacketReceived.</param>
    /// <param name="onDisconnected">Callback for onDisconnected.</param>
    /// <param name="handle">ID of the connection.</param>
    private static void ListenForData(SslStream stream, delegate* unmanaged<int, int, byte*, void> onPacketReceived, delegate* unmanaged<int, void> onDisconnected, int handle)
    {
        byte[] buffer = new byte[1024];

        try
        {
            while (true)
            {
                if (!stream.CanRead)
                {
                    break;
                }

                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                // Check if the server closed the connection.
                if (bytesRead == 0)
                {
                    Console.WriteLine("Server closed the connection.");
                    onDisconnected(handle);
                    break;
                }

                int realSize = GetPacketSize(buffer);
                byte[] receivedData = new byte[realSize];
                buffer.AsSpan(0, realSize).CopyTo(receivedData);

                if (bytesRead > 0)
                {
                    fixed (byte* dataPtr = receivedData)
                    {
                        if (realSize == 0)
                        {
                            Console.WriteLine("Receiving packet with 0 length...");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine($"[MuJS] S->C {BytesToHex(receivedData)}");
                            Console.ResetColor();
                            onPacketReceived(handle, bytesRead, dataPtr);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while listening for data: {ex.Message}");

            // Call the onDisconnected delegate when an error occurs
            onDisconnected(handle);
        }
    }

    /// <summary>
    /// Converts byte array to hex string.
    /// </summary>
    /// <param name="bytes">The buffer.</param>
    /// <returns>HEX string.</returns>
    private static string BytesToHex(byte[] bytes)
    {
        if (bytes == null)
        {
            return string.Empty;
        }

        StringBuilder hex = new(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            hex.AppendFormat("{0:X2} ", b);
        }

        return hex.ToString();
    }
}