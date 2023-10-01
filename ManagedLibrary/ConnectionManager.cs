// <copyright file="ConnectionManager.cs" company="MuJS">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MuJS;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
    private static readonly Dictionary<int, NetworkStream> Connections = new();

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
            NetworkStream stream = client.GetStream();

            Console.WriteLine("Connected to the server.");
            var handle = Interlocked.Increment(ref _maxHandle);
            Connections.Add(handle, stream);

            // Create a thread to listen for incoming data
            Thread listenThread = new(() => ListenForData(stream, onPacketReceived, onDisconnected, handle));
            listenThread.Start();

            return handle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error establishing connection: {ex}");
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
                Debug.WriteLine("Sent {0} bytes with handle {1}", count, handle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending {0} bytes with handle {1}: {2}", count, handle, ex);
            }
        }
        else
        {
            Debug.WriteLine("Connection with handle {0} not found.", handle);
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
    private static void ListenForData(NetworkStream stream, delegate* unmanaged<int, int, byte*, void> onPacketReceived, delegate* unmanaged<int, void> onDisconnected, int handle)
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
                int realSize = GetPacketSize(buffer);
                byte[] receivedData = new byte[realSize];
                buffer.AsSpan(0, realSize).CopyTo(receivedData);

                if (bytesRead > 0)
                {
                    fixed (byte* dataPtr = receivedData)
                    {
                        if (realSize == 0)
                        {
                            Debug.WriteLine("Receiving packet with 0 length...");
                        }
                        else
                        {
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
}