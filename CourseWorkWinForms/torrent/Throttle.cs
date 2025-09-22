using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Bittorrent;
public struct Item
{
	public DateTime Time;
	public long Size;
	public Peer Peer;
}
public class Throttle
{
	public long MaximumSize { get; set; }
	public TimeSpan MaximumWindow { get; set; }

	public long Value
	{
		get
		{
			lock (itemLock)
			{
				DateTime cutoff = DateTime.UtcNow.Add(-this.MaximumWindow);
				items.RemoveAll(x => x.Time < cutoff);
				return items.Sum(x => x.Size);
			}
		}
	}


	private object itemLock = new object();
	private List<Item> items = new List<Item>();

	public List<Item> Items
	{
		get
		{
			DateTime cutoff = DateTime.UtcNow.Add(-this.MaximumWindow);
			items.RemoveAll(x => x.Time < cutoff);
			return items;
		}
	}
	public Throttle(int maxSize, TimeSpan maxWindow)
	{
		MaximumSize = maxSize;
		MaximumWindow = maxWindow;
	}

	public void Add(long size)
	{
		lock (itemLock)
		{
			items.Add(new Item() { Time = DateTime.UtcNow, Size = size });
		}
	}

	public void Add(long size, Peer peer)
	{
		lock (itemLock)
		{
			items.Add(new Item() { Time = DateTime.UtcNow, Size = size, Peer = peer });
		}
	}

	public bool IsThrottled
	{
		get
		{
			lock (itemLock)
			{
				DateTime cutoff = DateTime.UtcNow.Add(-this.MaximumWindow);
				items.RemoveAll(x => x.Time < cutoff);
				return items.Sum(x => x.Size) >= MaximumSize;
			}
		}
	}

}