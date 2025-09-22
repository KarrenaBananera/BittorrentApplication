using BaseLibS.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;

public class Client
{
	public int maxLeechers = 200;
	public int maxSeeders = 200;
	TcpListener listener;
	public  int maxUploadBytesPerSecond = 16384 * 100;
	public  int maxDownloadBytesPerSecond = 16384 * 100;

	private static TimeSpan peerTimeout = TimeSpan.FromSeconds(30);
	public int Port { get; private set; }
	public Torrent Torrent { get; private set; }

	private bool isStopping;
	private int isProcessPeers = 0;
	private int isProcessUploads = 0;
	private int isProcessDownloads = 0;

	public string Id { get; private set; }

	private Random random = new Random();
	public ConcurrentDictionary<string, Peer> Peers { get; } = new();
	public ConcurrentDictionary<string, Peer> Seeders { get; } = new();
	public ConcurrentDictionary<string, Peer> Leechers { get; } = new();

	public Throttle uploadThrottle;
	public Throttle downloadThrottle;

	private ConcurrentQueue<DataRequest> OutgoingBlocks = new ConcurrentQueue<DataRequest>();
	private ConcurrentQueue<DataPackage> IncomingBlocks = new ConcurrentQueue<DataPackage>();
	public Client(int port, string torrentPath, string downloadPath)
	{
		Id = "";
		for (int i = 0; i < 20; i++)
			Id += (random.Next(0, 10));

		Port = port;

		downloadThrottle = new Throttle(maxDownloadBytesPerSecond, TimeSpan.FromSeconds(1));
		uploadThrottle = new Throttle(maxUploadBytesPerSecond, TimeSpan.FromSeconds(1));

		Torrent = BDecoder.LoadFromFile(torrentPath, downloadPath);
		Torrent.PieceVerified += HandlePieceVerified;
		foreach (var tracker in Torrent.Trackers)
		{
			tracker.PeerListUpdated += HandlePeerListUpdated;
		}

		Console.Error.WriteLine("Torrent was created: " + Torrent.Name);
		Console.Error.WriteLine("ALL Pieces : " + Torrent.PieceCount);
		Console.Error.WriteLine("Pieces to download: " + Torrent.IsPieceVerified.Count(x => x==false));

		Console.Error.WriteLine("Piece size: " + Torrent.PieceSize);
		Console.Error.WriteLine("Total size: " + FileItem.BytesToString(Torrent.TotalSize));
		Console.Error.WriteLine("download left: " + (Torrent.Progress * 100));

		if (Torrent.Trackers.Count == 0)
			Console.Error.WriteLine("No trackers has found");


	}
	public void Stop()
	{
		Console.Error.WriteLine("stopping client");

		isStopping = true;
		DisablePeerConnections();
		Torrent.UpdateTrackers(Id, Port);
	}

	private void DisablePeerConnections()
	{
		listener.Stop();
		listener = null;

		foreach (var peer in Peers)
			peer.Value.Disconnect();

		Console.Error.WriteLine("stopped listening for incoming peer connections on port " + Port);
	}

	public void Start()
	{
		Console.Error.WriteLine("starting client");

		isStopping = false;


		EnablePeerConnections();

		// tracker 
		new Thread(new ThreadStart(() =>
		{
			while (!isStopping)
			{
				Torrent.UpdateTrackers(Id, Port);
				//Thread.Sleep(500);
				Thread.Sleep(5000);

			}
		})).Start();

		// peer 
		new Thread(new ThreadStart(() =>
		{
			while (!isStopping)
			{
				ProcessPeers();
				//Thread.Sleep(1000);
				Thread.Sleep(500);

			}
		})).Start();
		// upload 
		new Thread(new ThreadStart(() =>
		{
			while (!isStopping)
			{
				ProcessUploads();
				Thread.Sleep(1000);
			}
		})).Start();

		// download thread
		new Thread(new ThreadStart(() =>
		{
			while (!isStopping)
			{
				ProcessDownloads();
				//Thread.Sleep(1000);
				Thread.Sleep(500);

			}
		})).Start();
	}
	private void HandlePieceVerified(object sender, int index)
	{
		ProcessPeers();

		foreach (var peer in Peers)
		{
			if (!peer.Value.IsHandshakeReceived || !peer.Value.IsHandshakeSent)
				continue;

			peer.Value.SendHave(index);
		}
	}
	private void EnablePeerConnections()
	{
		listener = new TcpListener(new IPEndPoint(IPAddress.Any, Port));
		listener.Start();
		listener.BeginAcceptTcpClient(new AsyncCallback(HandleNewConnection), null);

		Console.Error.WriteLine("started listening for incoming peer connections on port " + Port);
	}

	private void HandleNewConnection(IAsyncResult ar)
	{
		if (listener == null)
			return;

		TcpClient client = listener.EndAcceptTcpClient(ar);
		listener.BeginAcceptTcpClient(new AsyncCallback(HandleNewConnection), null);

		AddPeer(new Peer(Torrent, Id, client));
	}
	private void ProcessPeers()
	{
		if (Interlocked.Exchange(ref isProcessPeers, 1) == 1)
			return;
		KeyValuePair<string, Peer>[] peers = null;
		try
		{
			peers = Peers.OrderByDescending(x => x.Value.PiecesRequiredAvailable).ToArray();
		}
		catch(Exception e)
		{
			Interlocked.Exchange(ref isProcessPeers, 0);
			return;
		}
		foreach (var peer in peers)
		{
			if (DateTime.UtcNow > peer.Value.LastActive.Add(peerTimeout))
			{
				peer.Value.Disconnect();
				continue;
			}

			if (!peer.Value.IsHandshakeSent || !peer.Value.IsHandshakeReceived)
				continue;

			if (Torrent.IsCompleted)
				peer.Value.SendNotInterested();
			else
				peer.Value.SendInterested();

			if (peer.Value.IsCompleted && Torrent.IsCompleted)
			{
				peer.Value.Disconnect();
				continue;
			}

			peer.Value.SendKeepAlive();

			if (Torrent.IsStarted && Leechers.Count <= maxLeechers)
			{
				if (peer.Value.IsInterestedReceived && peer.Value.IsChokeSent)
					peer.Value.SendUnchoke();
			}

			if (!Torrent.IsCompleted && Seeders.Count <= maxSeeders)
			{
				if (!peer.Value.Chocked)
					Seeders.TryAdd(peer.Key, peer.Value);
			}
		}

		Interlocked.Exchange(ref isProcessPeers, 0);
	}

	private void HandlePeerListUpdated(object sender, List<IPEndPoint> endPoints)
	{
		IPAddress local = LocalIPAddress;

		foreach (var endPoint in endPoints)
		{
			if (endPoint.Address.Equals(local) && endPoint.Port == Port)
				continue;

			AddPeer(new Peer(Torrent, Id, endPoint));
		}

		Console.Error.WriteLine("received peer information from " + ((Tracker)sender).Address);
		Console.Error.WriteLine((string)("peer count: " + Peers.Count));
	}

	private void AddPeer(Peer peer)
	{
		peer.BlockRequested += HandleBlockRequested;
		peer.BlockCancelled += HandleBlockCancelled;
		peer.BlockReceived += HandleBlockReceived;
		peer.Disconnected += HandlePeerDisconnected;
		peer.StateChanged += HandlePeerStateChanged;

		peer.Connect();

		if (!Peers.TryAdd(peer.Key, peer))
			peer.Disconnect();
	}

	private void HandlePeerDisconnected(object sender, EventArgs args)
	{
		Peer peer = sender as Peer;

		peer.BlockRequested -= HandleBlockRequested;
		peer.BlockCancelled -= HandleBlockCancelled;
		peer.BlockReceived -= HandleBlockReceived;
		peer.Disconnected -= HandlePeerDisconnected;
		peer.StateChanged -= HandlePeerStateChanged;

		Peer tmp;
		Peers.TryRemove(peer.Key, out tmp);
		Seeders.TryRemove(peer.Key, out tmp);
		Leechers.TryRemove(peer.Key, out tmp);
	}

	private void HandlePeerStateChanged(object sender, EventArgs args)
	{
		ProcessPeers();
	}
	private static IPAddress LocalIPAddress
	{
		get
		{
			var host = Dns.GetHostEntry(Dns.GetHostName());
			foreach (var ip in host.AddressList)
			{
				if (ip.AddressFamily == AddressFamily.InterNetwork)
					return ip;
			}
			throw new Exception("Local IP Address Not Found!");
		}
	}

	private void ProcessUploads()
	{
		if (Interlocked.Exchange(ref isProcessUploads, 1) == 1)
			return;

		DataRequest block;
		while (!uploadThrottle.IsThrottled && OutgoingBlocks.TryDequeue(out block))
		{
			if (block.IsCancelled)
				continue;

			if (!Torrent.IsPieceVerified[block.Piece])
				continue;

			byte[] data = Torrent.ReadBlock(block.Piece, block.Begin, block.Length);
			if (data == null)
				continue;

			block.Peer.SendPiece(block.Piece, block.Begin, data);
			uploadThrottle.Add(block.Length);
			Torrent.Uploaded += block.Length;
		}

		Interlocked.Exchange(ref isProcessUploads, 0);
	}

	private void HandleBlockRequested(object sender, DataRequest block)
	{
		OutgoingBlocks.Enqueue(block);

		ProcessUploads();
	}
	private void HandleBlockCancelled(object sender, DataRequest block)
	{
		foreach (var item in OutgoingBlocks)
		{
			if (item.Peer != block.Peer || item.Piece != block.Piece || item.Begin != block.Begin || item.Length != block.Length)
				continue;

			item.IsCancelled = true;
		}

		ProcessUploads();
	}

	private void HandleBlockReceived(object sender, DataPackage args)
	{
		IncomingBlocks.Enqueue(args);

		args.Peer.IsBlockRequested[args.Piece][args.Block] = false;

		foreach (var peer in Peers)
		{
			if (!peer.Value.IsBlockRequested[args.Piece][args.Block])
				continue;

			peer.Value.SendCancel(args.Piece, args.Block * Torrent.BlockSize, Torrent.BlockSize);
			peer.Value.IsBlockRequested[args.Piece][args.Block] = false;
		}

		ProcessDownloads();
	}

	private void ProcessDownloads()
	{
		if (Interlocked.Exchange(ref isProcessDownloads, 1) == 1)
			return;

		DataPackage incomingBlock;
		while (IncomingBlocks.TryDequeue(out incomingBlock))
			Torrent.WriteBlock(incomingBlock.Piece, incomingBlock.Block, incomingBlock.Data);

		if (Torrent.IsCompleted)
		{
			Interlocked.Exchange(ref isProcessDownloads, 0);
			return;
		}

		int[] ranked = GetRankedPieces();

		foreach (var piece in ranked)
		{
			if (Torrent.IsPieceVerified[piece])
				continue;

			foreach (var peer in GetRankedSeeders())
			{
				if (!peer.IsPieceDownloaded[piece])
					continue;

				for (int block = 0; block < Torrent.GetBlockCount(piece); block++)
				{
					if (downloadThrottle.IsThrottled)
						continue;

					if (Torrent.IsBlockAcquired[piece][block])
						continue;

					if (peer.BlocksRequested > 1)
						continue;

					
					if (Peers.Count(x => x.Value.IsBlockRequested[piece][block]) > 2)
						continue;

					int size = Torrent.GetBlockSize(piece, block);
					peer.SendRequest(piece, block * Torrent.BlockSize, size);
					downloadThrottle.Add(size, peer);
					peer.IsBlockRequested[piece][block] = true;
				}
			}
		}

		Interlocked.Exchange(ref isProcessDownloads, 0);
	}

	private Peer[] GetRankedSeeders()
	{
		return Seeders.Values.Concat(Peers.Values).Distinct().OrderBy(x => random.Next(0, 100)).ToArray();
	}

	private int[] GetRankedPieces()
	{
		var indexes = Enumerable.Range(0, Torrent.PieceCount).ToArray();
		var scores = indexes.Select(x => GetPieceScore(x)).ToArray();

		Array.Sort(scores, indexes);
		Array.Reverse(indexes);

		return indexes;
	}

	private double GetPieceScore(int piece)
	{
		double progress = GetPieceProgress(piece);
		double rarity = GetPieceRarity(piece);


		double rand = random.Next(0, 100) / 1000.0;

		return -progress*2  + rarity + rand;
	}

	private double GetPieceRarity(int index)
	{
		if (Peers.Count < 1)
			return 0.0;

		return Peers.Average(x => x.Value.IsPieceDownloaded[index] ? 0.0 : 1.0);
	}

	private double GetPieceProgress(int index)
	{
		return Torrent.IsBlockAcquired[index].Average(x => x ? 1.0 : 0.0);
	}

}
