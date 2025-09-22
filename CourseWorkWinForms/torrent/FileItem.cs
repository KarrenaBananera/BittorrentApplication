using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;


	public class FileItem
	{
		public string Path;
		public long Size;
		public long Offset;

		public string FormattedSize { get { return BytesToString(Size); } }

	public static string BytesToString(long bytes)
	{
		string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; // Можете добавить больше при необходимости

		if (bytes == 0)
			return "0" + suffixes[0];

		long absBytes = Math.Abs(bytes);
		int suffixIndex = (int)(Math.Log(absBytes, 1024));
		double num = absBytes / Math.Pow(1024, suffixIndex);

		// Ограничиваем индекс, чтобы не выйти за пределы массива
		suffixIndex = Math.Min(suffixIndex, suffixes.Length - 1);

		// Форматируем число, убирая лишние нули после запятой
		string formattedNum = num.ToString("0.##");
		if (bytes < 0)
			formattedNum = "-" + formattedNum;

		return formattedNum + " " + suffixes[suffixIndex];
	}

	// Перегрузка для int
	public static string BytesToString(int bytes)
	{
		return BytesToString((long)bytes);
	}
}
