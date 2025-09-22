using Bittorrent;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TorrentClientUI;

namespace TorrentClientUI;

public class PropertiesForm : Form
{
	public int MaxSeeds { get; private set; }
	public int MaxLeeches { get; private set; }
	public int MaxDownloadSpeed { get; private set; }

	public PropertiesForm(Client torrent)
	{
		InitializeComponents(torrent);
	}

	private void InitializeComponents(Client torrent)
	{
		this.Text = "Свойства торрента";
		this.Size = new Size(300, 200);
		this.FormBorderStyle = FormBorderStyle.FixedDialog;
		this.StartPosition = FormStartPosition.CenterParent;
		this.MaximizeBox = false;
		this.MinimizeBox = false;

		// Параметры
		var lblSeeds = new Label { Text = "Макс. сидов:", Top = 20, Left = 20 };
		var numSeeds = new NumericUpDown
		{
			Minimum = 1,
			Maximum = 500,
			Value = torrent.maxSeeders,
			Left = 150,
			Top = 20,
			Width = 100
		};

		var lblLeeches = new Label { Text = "Макс. личей:", Top = 50, Left = 20 };
		var numLeeches = new NumericUpDown
		{
			Minimum = 1,
			Maximum = 500,
			Value = torrent.maxLeechers,
			Left = 150,
			Top = 50,
			Width = 100
		};

		var lblSpeed = new Label { Text = "Макс. скорость KB/s:", Top = 80, Left = 20, Width = 130 };
		var numSpeed = new NumericUpDown
		{
			Minimum = 1,
			Maximum = Int32.MaxValue,
			Value = torrent.downloadThrottle.MaximumSize / 1000,
			Left = 150,
			Top = 80,
			Width = 100
		};

		// Кнопки
		var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 50, Top = 120 };
		var btnCancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Left = 150, Top = 120 };

		// Добавление элементов
		this.Controls.AddRange(new Control[]
		{
			lblSeeds, numSeeds,
			lblLeeches, numLeeches,
			lblSpeed, numSpeed,
			btnOk, btnCancel
		});

		// Обработчики
		btnOk.Click += (s, e) =>
		{
			MaxSeeds = (int)numSeeds.Value;
			MaxLeeches = (int)numLeeches.Value;
			MaxDownloadSpeed = (int)numSpeed.Value;
			this.DialogResult = DialogResult.OK;
			this.Close();
		};

		btnCancel.Click += (s, e) => this.Close();
	}
}
