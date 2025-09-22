using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using BaseLibS.Parse.Endian;

namespace Bittorrent;
public class Tracker
{
	public event EventHandler<List<IPEndPoint>> PeerListUpdated;
	public DateTime LastPeerRequest { get; private set; } = DateTime.MinValue;
	public TimeSpan PeerRequestInterval { get; private set; } = TimeSpan.FromMinutes(0.5);
	public string Address { get; private set; }

	public List<IPEndPoint> PeerList = new List<IPEndPoint>();
	private Torrent _torrent;
	public Tracker(string address, Torrent torrent)
	{
		Address = address;
		_torrent = torrent;
	}

	public void Update(Torrent torrent, string id, int port)
	{
	
		if (
			DateTime.UtcNow < LastPeerRequest.Add(PeerRequestInterval))
			return;

		LastPeerRequest = DateTime.UtcNow;

		string url = String.Format("{0}?info_hash={1}&peer_id={2}&port={3}&uploaded={4}&downloaded={5}&left={6}&event={7}&compact=1",
					 Address, torrent.UrlSafeStringInfohash,
					 id, port,
					 torrent.Uploaded, torrent.Downloaded, torrent.Left,
					 "started");
		Console.Error.WriteLine("Send connection to " + Address);
		var uri = new Uri(Address);
		if (uri.Scheme.ToLower() == "udp")
		{
			RequestUdp(uri,torrent,id,port);
		}
		else {
			Request(url);
		}
	}

	private async void RequestUdp(Uri uri, Torrent torrent, string id, int port)
	{
		var peersGetter = new PeersGetter();
		PeerList = await peersGetter.GetPeersUdp(new UdpClient(), uri.DnsSafeHost,
			uri.Port, torrent.Infohash,
			Encoding.UTF8.GetBytes(id), torrent.Downloaded, torrent.Left, torrent.Uploaded, port);
		PeerListUpdated?.Invoke(this, PeerList);
	}
	private async void Request(string url)
	{
		var peersGetter = new PeersGetter();
		var response = await peersGetter.GetDataAsyncHttp(url);
		if (response == null)
			return;
		Dictionary<string, object> info = null;
		try
		{
			info = BDecoder.Decode(response) as Dictionary<string, object>;
		}
		catch
		{
			Console.WriteLine("unable to decode tracker announce response");
			return;
		}
		if (info == null)
		{
			Console.WriteLine("unable to decode tracker announce response");
			return;
		}
		if (info.ContainsKey("interval"))
			PeerRequestInterval = TimeSpan.FromSeconds((long)info["interval"]);
		else
			PeerRequestInterval = TimeSpan.FromSeconds(60);
		byte[] peerInfo = new byte[0];
		if (info.ContainsKey("peers"))
			peerInfo = (byte[])info["peers"];

		List<IPEndPoint> peers = new List<IPEndPoint>();
		for (int i = 0; i < peerInfo.Length / 6; i++)
		{
			int offset = i * 6;
			string address =
				peerInfo[offset] + "." +
				peerInfo[offset + 1] + "." +
				peerInfo[offset + 2] + "." +
				peerInfo[offset + 3];
			int port = EndianBitConverter.Big.ToChar(peerInfo, offset + 4);

			peers.Add(new IPEndPoint(IPAddress.Parse(address), port));
		}

		var handler = PeerListUpdated;
		if (handler != null)
			handler(this, peers);

		PeerList = peers;
		Console.Error.WriteLine("Get response from " + Address + " Peers: " + PeerList.Count);
	}
}