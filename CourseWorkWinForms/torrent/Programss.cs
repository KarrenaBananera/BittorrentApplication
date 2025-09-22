//using System;
//using System.Net;
//using System.Security.Cryptography;
//using System.Text;
//using System.Text.Json;
//using BaseLibS.Num;
//using Bittorrent;

//#region ss

////if (true)
////{

////    var (command, param) = args.Length switch
////    {
////        0 => throw new InvalidOperationException("Usage: your_program.sh <command> <param>"),
////        1 => throw new InvalidOperationException("Usage: your_program.sh <command> <param>"),
////        _ => (args[0], args[1])
////    };

////    if (command == "decode")
////    {


////        var encodedValue = Encoding.UTF8.GetBytes(param); ;
////        Console.WriteLine(JsonSerializer.Serialize(BDecoder.DecodeTest(encodedValue)));

////    }
////    if (command == "info")
////    {
////        Torrent torrent = BDecoder.LoadFromFile(param, "");
////        Console.WriteLine("Tracker URL: " + torrent.Trackers.First().Address);
////        Console.WriteLine("Length: " + torrent.TotalSize);
////        Console.WriteLine("Info Hash: " + BDecoder.ToHex(torrent.Infohash));
////        Console.WriteLine("Piece Length: " + torrent.PieceSize);

////        Console.WriteLine("Piece Hashes:");

////        foreach (var hash in torrent.PieceHashes)
////        {
////            Console.WriteLine(BDecoder.ToHex(hash));
////        }
////    }

////    if (command == "peers")
////    {
////        Torrent torrent = BDecoder.LoadFromFile(param, "");
////        torrent.UpdateTrackers(randomid, 6881);
////        using (var resetEvent = new ManualResetEvent(false))
////        {
////            torrent.PeerListUpdated += (x, y) => resetEvent.Set();
////            resetEvent.WaitOne();
////        }

////        foreach (var tracker in torrent.Trackers)
////        {
////            foreach (var peer in tracker.PeerList)
////            {
////                Console.WriteLine(peer.ToString());
////            }
////        }
////    }
////    if (command == "handshake")
////    {
////        var fileName = args[1];
////        var ipEndPoint = args[2];
////        IPEndPoint iPEndPointType = IPEndPoint.Parse(ipEndPoint);

////        Torrent torrent = BDecoder.LoadFromFile(fileName, "");

////        torrent.UpdateTrackers(randomid, 6881);
////        /*
////		using (var resetEvent = new ManualResetEvent(false))
////		{
////			torrent.PeerListUpdated += (x, y) => resetEvent.Set();
////			resetEvent.WaitOne();
////		}*/
////        Peer targetPeer = new Peer(torrent, randomid, iPEndPointType);
////        targetPeer.Connect();

////        while (targetPeer.IsHandshakeReceived != true)
////        { }

////        Console.WriteLine("Peer ID: " + targetPeer.Id);

////    }
////    //come
////    if (command == "download_piece")
////    {
////        string actualFile = args[2];
////        string fileName = args[2];
////        string fileTorrent = args[3];
////        int piecenum = Int32.Parse(args[4]);

////        Torrent torrent = BDecoder.LoadFromFile(fileTorrent, Path.GetDirectoryName(fileName));

////        torrent.UpdateTrackers(randomid, 6881);
////        List<IPEndPoint> peers = new();

////        using (var resetEvent = new ManualResetEvent(false))
////        {
////            torrent.PeerListUpdated += (x, y) => resetEvent.Set();
////            torrent.PeerListUpdated += (x, y) => peers = y;
////            resetEvent.WaitOne();
////        }

////        Peer peer2 = new Peer(torrent, randomid, peers.First());
////        peer2.Connect();
////        //while (peer2.IsHandshakeReceived == false) { }
////        Thread.Sleep(400);
////        peer2.SendInterested();

////        //while (peer2.Chocked) { }
////        Thread.Sleep(400);
////        for (int block = 0; block < torrent.GetBlockCount(piecenum); block++)
////        {
////            int size = torrent.GetBlockSize(piecenum, block);
////            peer2.SendRequest(piecenum, block * torrent.BlockSize, size);
////            peer2.IsBlockRequested[piecenum][block] = true;
////        }
////        void Peer_BlockReceived(object? sender, DataPackage e)
////        {
////            torrent.WriteBlock(e.Piece, e.Block, e.Data);
////        }
////        peer2.BlockReceived += Peer_BlockReceived;

////        //while (torrent.IsPieceVerified[piecenum]) { }
////        Thread.Sleep(1000);
////        File.WriteAllBytes(fileName, torrent.ReadPiece(piecenum));
////        Console.WriteLine("done");
////    }

////    if (command == "download")
////    {
////		string fullFileName = args[2];
////		string torrentPath = args[3];
////		Client client = new Client(6118, torrentPath, Path.GetDirectoryName(fullFileName));
////		client.Start();
////		using (var resetEvent = new ManualResetEvent(false))
////		{
////			client.Torrent.FilesDownloaded += (x, y) => resetEvent.Set();
////			resetEvent.WaitOne();
////		}
////		Console.Error.WriteLine("END");
////		client.Stop();
////	}
////}



//#endregion


//Console.WriteLine("Предоставьте путь к папке для установки");
//string fullFileName = @"D:\New folder";
////string fullFileName = "D:\\New folder";
//Console.WriteLine("укажите путь к торренту");
//string torrentPath = @"C:\Users\super\Downloads\1975754.torrent";

////string torrentPath = @"C:\Users\super\Downloads\1975754.torrent";
//Client client = new Client(6119, torrentPath, fullFileName);
//client.Start();

//using (var resetEvent = new ManualResetEvent(false))
//{
//	client.Torrent.FilesDownloaded += (x, y) => resetEvent.Set();
//	resetEvent.WaitOne();
//}

//Thread.Sleep(10000000);
//Console.Error.WriteLine("END");
//client.Stop();