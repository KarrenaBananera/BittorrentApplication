using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;

public class PeersGetter
{
	private static readonly HttpClient _httpClient = new HttpClient();

	public async Task<byte[]> GetDataAsyncHttp(string url)
	{
		try
		{
			HttpResponseMessage response = await _httpClient.GetAsync(url);
			response.EnsureSuccessStatusCode();
			return await response.Content.ReadAsByteArrayAsync();
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine($"Ошибка HTTP запроса: {ex.Message}");
			return null;
		}
		catch (SocketException ex)
		{
			Console.WriteLine($"Сетевая ошибка: {ex.Message}");
			return null;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Общая ошибка: {ex.Message}");
			return null;
		}
	}

	public async Task<List<IPEndPoint>> GetPeersUdp(UdpClient client,
		string trackerHost, int trackerPort,
		byte[] infoHash, byte[] peerId, long downloaded, long left, long uploaded, int port)
	{
		try
		{
			return await GetPeers(client, trackerHost, trackerPort, infoHash, peerId, downloaded, left, uploaded, port);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine("Failed to get peers from:" + trackerHost);
			Console.Error.WriteLine("Exception:" + e.Message);
			return new List<IPEndPoint>();
		}
	}

	private (byte[], int) CreateConnectRequest()
	{
		using (var ms = new MemoryStream())
		using (var writer = new BinaryWriter(ms))
		{
			byte[] magic = { 0x00, 0x00, 0x04, 0x17, 0x27, 0x10, 0x19, 0x80 };
			writer.Write(magic);

			writer.Write(IPAddress.HostToNetworkOrder(0)); // action = connect

			int transactionId = new Random().Next();
			writer.Write(IPAddress.HostToNetworkOrder(transactionId));

			return (ms.ToArray(), transactionId);
		}
	}

	private async Task<byte[]> SendConnectRequest(UdpClient client, string trackerHost, int trackerPort)
	{
		Console.Error.WriteLine($"Send request to: {trackerHost}:{trackerPort}");
		var (connectRequest, transactionIdSent) = CreateConnectRequest();
		client.Send(connectRequest, connectRequest.Length, trackerHost, trackerPort);

		var receiveTask = client.ReceiveAsync();
		//receiveTask.Wait(15000);
		var result = await receiveTask;

		if (!receiveTask.IsCompleted)
			throw new TimeoutException($"Connect request timeout to {trackerHost}");

		byte[] response = receiveTask.Result.Buffer;
		if (response.Length < 16)
			throw new Exception("Invalid response length");

		// Проверка action (должен быть 0 - connect)
		int action = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response, 0));
		if (action != 0)
			throw new Exception($"Unexpected action: {action} (expected 0)");

		// Проверка transaction_id
		int transactionIdReceived = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response, 4));
		if (transactionIdReceived != transactionIdSent)
			throw new Exception("Transaction ID mismatch");

		// Возвращаем connection_id как есть (big-endian)
		byte[] connectionId = new byte[8];
		Buffer.BlockCopy(response, 8, connectionId, 0, 8);
		return connectionId;
	}

	byte[] CreateAnnounceRequest(
		byte[] connectionId,
		byte[] infoHash,
		byte[] peerId,
		long downloaded,
		long left,
		long uploaded,
		int port,
		int transactionId)
	{
		using (var ms = new MemoryStream())
		using (var writer = new BinaryWriter(ms))
		{
			writer.Write(connectionId);          // Connection ID (8 байт)
			writer.Write(IPAddress.HostToNetworkOrder(1));     // Action = announce
			writer.Write(IPAddress.HostToNetworkOrder(transactionId)); // Transaction ID
			writer.Write(infoHash);              // Info Hash (20 байт)
			writer.Write(peerId);                // Peer ID (20 байт)
			writer.Write(IPAddress.HostToNetworkOrder(downloaded)); // Downloaded
			writer.Write(IPAddress.HostToNetworkOrder(left));       // Left
			writer.Write(IPAddress.HostToNetworkOrder(uploaded));   // Uploaded
			writer.Write(IPAddress.HostToNetworkOrder(2));     // Event = started
			writer.Write(0);                         // IP address (0 = default)
			writer.Write(new Random().Next());       // Key
			writer.Write(IPAddress.HostToNetworkOrder(-1));    // Num want (-1 = default)

			// Порядок байт для порта: big-endian
			writer.Write((byte)(port >> 8));    // Старший байт
			writer.Write((byte)port);           // Младший байт

			return ms.ToArray();
		}
	}

	private async Task<List<IPEndPoint>> GetPeers(UdpClient client,
		string trackerHost, int trackerPort,
		byte[] infoHash, byte[] peerId, long downloaded, long left, long uploaded, int port)
	{
		byte[] connectionId = await SendConnectRequest(client, trackerHost, trackerPort);

		int transactionId = new Random().Next();
		byte[] announceRequest = CreateAnnounceRequest(
			connectionId,
			infoHash,
			peerId,
			downloaded,
			left,
			uploaded,
			port,
			transactionId
		);

		client.Send(announceRequest, announceRequest.Length, trackerHost, trackerPort);

		var receiveTask = client.ReceiveAsync();
		//receiveTask.Wait(15000);
		var result = await receiveTask;
		if (!receiveTask.IsCompleted)
			throw new TimeoutException("Announce request timed out");
		List<IPEndPoint> peers = new List<IPEndPoint>();
			byte[] response = receiveTask.Result.Buffer;

			// Парсим ответ:
			// - action (4 байта, должно быть 1)
			// - transaction_id должен совпадать
			// - Список пиров (начиная с 20-го байта)
			int offset = 20; // Пропускаем action, transaction_id, interval, leechers, seeders
			string str = Encoding.UTF8.GetString(response);
		string bytes = string.Join(" ", response);
		int i = 0;
		while (offset < response.Length)
			{
				byte[] ipBytes = new byte[4];
				Array.Copy(response, offset, ipBytes, 0, 4);
				IPAddress ip = new IPAddress(ipBytes);

				ushort peerPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(response, offset + 4));

				peers.Add(new IPEndPoint(ip, peerPort));
				offset += 6;
			}
		return peers;
	}
}

