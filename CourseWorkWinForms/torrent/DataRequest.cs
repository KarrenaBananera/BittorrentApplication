using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;

public class DataRequest
{
	public Peer Peer;
	public int Piece;
	public int Begin;	
	public int Length;
	public bool IsCancelled;
}
