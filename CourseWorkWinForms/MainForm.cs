using Bittorrent;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Windows.Forms;

namespace TorrentClientUI;

public partial class MainForm : Form
{
	// Мок-данные
	private List<Client> torrents = new List<Client>();
	private System.Windows.Forms.Timer updateTimer = new();

	public MainForm()
	{
		InitializeComponent();
		SetupUI();
		SetupTimer();
	}

	private void SetupUI()
	{
		// Настройка главного окна
		this.Text = "Torrent Client";
		this.Size = new Size(800, 600);
		this.AllowDrop = true;
		this.DragEnter += MainForm_DragEnter;
		this.DragDrop += MainForm_DragDrop;

		// Контейнер с разделением
		SplitContainer splitContainer = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Horizontal,
			SplitterDistance = 300
		};
		this.Controls.Add(splitContainer);

		// Список торрентов
		torrentsListView = new ListView
		{
			Dock = DockStyle.Fill,
			View = View.Details,
			FullRowSelect = true,
			GridLines = true,
			ContextMenuStrip = new ContextMenuStrip()
		};
		torrentsListView.Columns.AddRange(new[]
		{
			new ColumnHeader { Text = "Название", Width = 200 },
			new ColumnHeader { Text = "Размер", Width = 100 },
			new ColumnHeader { Text = "Прогресс", Width = 100 },
			new ColumnHeader { Text = "Скорость", Width = 100 }
		});
		torrentsListView.SelectedIndexChanged += TorrentsListView_SelectedIndexChanged;
		splitContainer.Panel1.Controls.Add(torrentsListView);

		// Контекстное меню для торрентов
		var removeMenu = new ToolStripMenuItem("Удалить");
		removeMenu.Click += RemoveMenu_Click;
		var propertiesMenu = new ToolStripMenuItem("Свойства");
		propertiesMenu.Click += PropertiesMenu_Click;
		torrentsListView.ContextMenuStrip.Items.AddRange(new[] { removeMenu, propertiesMenu });

		// Список пиров
		peersListView = new ListView
		{
			Dock = DockStyle.Fill,
			View = View.Details,
			FullRowSelect = true,
			GridLines = true
		};
		peersListView.Columns.AddRange(new[]
		{
			new ColumnHeader { Text = "IP:Порт", Width = 150 },
			new ColumnHeader { Text = "Скорость", Width = 100 }
		});
		splitContainer.Panel2.Controls.Add(peersListView);

		// Кнопка добавления
		var addButton = new Button
		{
			Text = "Добавить торрент",
			Dock = DockStyle.Top,
			Height = 30
		};
		addButton.Click += AddButton_Click;
		this.Controls.Add(addButton);
	}

	private void SetupTimer()
	{
		updateTimer.Interval = 5000; // 5 секунд
		updateTimer.Tick += UpdateTimer_Tick;
		updateTimer.Start();
	}


	// Обновление списка торрентов
	private void RefreshTorrentsList()
	{
		torrentsListView.Items.Clear();
		foreach (var torrent in torrents)
		{
			var item = new ListViewItem(torrent.Torrent.Name);
			item.SubItems.Add(torrent.Torrent.Size);
			item.SubItems.Add((torrent.Torrent.Progress * 100).ToString("0.0") + "%");
			item.SubItems.Add(FileItem.BytesToString(torrent.uploadThrottle.Value));
			item.Tag = torrent;
			torrentsListView.Items.Add(item);
		}
	}

	// Обновление списка пиров
	private void RefreshPeersList(Client torrent)
	{
		peersListView.Items.Clear();
		foreach (var peer in torrent.Seeders.Values.Concat(torrent.Leechers.Values))
		{
			var item = new ListViewItem($"{peer.IPEndPoint}");
			long totalSpeed = 0;
			foreach (var _item in torrent.downloadThrottle.Items) 
			{
				if (_item.Peer.IPEndPoint.Equals(peer.IPEndPoint))
					totalSpeed += _item.Size;
			}
			item.SubItems.Add(FileItem.BytesToString(totalSpeed));
			peersListView.Items.Add(item);
		}
	}

	// Обработчики событий
	private void TorrentsListView_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (torrentsListView.SelectedItems.Count > 0)
		{
			var selectedTorrent = torrentsListView.SelectedItems[0].Tag as Client;
			if (selectedTorrent != null)
			{
				RefreshPeersList(selectedTorrent);
			}
		}
	}

	private void AddButton_Click(object sender, EventArgs e)
	{
		using (OpenFileDialog ofd = new OpenFileDialog())
		{
			ofd.Filter = "Torrent files (*.torrent)|*.torrent";
			if (ofd.ShowDialog() == DialogResult.OK)
			{
				AddTorrentFile(ofd.FileName);
			}
		}
	}

	private void MainForm_DragEnter(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
			e.Effect = DragDropEffects.Copy;
	}

	private void MainForm_DragDrop(object sender, DragEventArgs e)
	{
		string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
		foreach (string file in files)
		{
			if (Path.GetExtension(file).ToLower() == ".torrent")
			{
				AddTorrentFile(file);
			}
		}
	}

	private void RemoveMenu_Click(object sender, EventArgs e)
	{
		if (torrentsListView.SelectedItems.Count > 0)
		{
			foreach (ListViewItem selected in torrentsListView.SelectedItems)
			{
				//var selected = torrentsListView.SelectedItems[0];
				var client = selected.Tag as Client;
				client.Stop();
				string filePath = client.Torrent.Name + ".torrent";
				File.Delete(filePath);
				File.Delete(filePath + ".bin");
				torrents.Remove(client);
				torrentsListView.Items.Remove(selected);
				peersListView.Items.Clear();
			}
		}
	}

	private void PropertiesMenu_Click(object sender, EventArgs e)
	{
		if (torrentsListView.SelectedItems.Count > 0)
		{
			var selectedTorrent = torrentsListView.SelectedItems[0].Tag as Client;
			using (var propForm = new PropertiesForm(selectedTorrent))
			{
				if (propForm.ShowDialog() == DialogResult.OK)
				{
					// Обновляем параметры торрента
					selectedTorrent.downloadThrottle.MaximumSize = propForm.MaxDownloadSpeed * 1000;
					selectedTorrent.maxLeechers = propForm.MaxLeeches;
					selectedTorrent.maxSeeders = propForm.MaxSeeds;
				}
			}
		}
	}

	private void UpdateTimer_Tick(object sender, EventArgs e)
	{
		// Мок-обновление данных
		foreach (ListViewItem item in torrentsListView.Items)
		{
			var torrent = item.Tag as Client;
			if (torrent != null)
			{
				item.SubItems[2].Text = (torrent.Torrent.Progress * 100).ToString("0.0") + "%";
				item.SubItems[3].Text = FileItem.BytesToString(torrent.downloadThrottle.Value);
			}
		}

		if (torrentsListView.SelectedItems.Count > 0)
		{
			var selectedTorrent = torrentsListView.SelectedItems[0].Tag as Client;
			RefreshPeersList(selectedTorrent);
		}
	}
	private void AddTorrentFile(string filePath)
	{
		if (File.Exists(Path.GetFileName(filePath) + ".bin"))
			return;
		string selectedPath;
		using (var folderDialog = new FolderBrowserDialog())
		{
			// Настройки диалога
			folderDialog.Description = "Выберите папку";
			folderDialog.RootFolder = Environment.SpecialFolder.MyComputer;
			folderDialog.ShowNewFolderButton = true;

			// Показываем диалог и проверяем результат
			if (folderDialog.ShowDialog() == DialogResult.OK)
			{
				selectedPath = folderDialog.SelectedPath;
				MessageBox.Show($"Выбрана папка: {selectedPath}", "Информация",
							   MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			else
			{
				return;
			}
		}
		try
		{
			if (!File.Exists(Path.GetFileName(filePath)))
				File.Copy(filePath, Path.GetFileName(filePath));
			var filestream = File.Create(Path.GetFileName(filePath) + ".bin");
			filestream.Dispose();
			File.WriteAllText(Path.GetFileName(filePath) + ".bin", selectedPath);
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "Error adding torrent",
							   MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		
		int port = GetAvailablePort();
		var newClient = new Client(port, filePath, selectedPath);
		newClient.Start();
		torrents.Add(newClient);
		RefreshTorrentsList();
	}

	private ListView torrentsListView;
	private ListView peersListView;


	private void MainForm_Load(object sender, EventArgs e)
	{
		string[] torrentFiles = Directory.GetFiles(Directory.GetCurrentDirectory(),"*.torrent");
		foreach (var file in torrentFiles)
		{
			if (!File.Exists(file + ".bin"))
				continue;

			var downloadPath = File.ReadAllText(file+".bin");
			var newClient = new Client(GetAvailablePort(), file, downloadPath);
			newClient.Start();
			torrents.Add(newClient);
		}
		RefreshTorrentsList();
	}

	public static int GetAvailablePort()
	{
		// Создаем временный TcpListener на порту 0 (система выберет свободный)
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();

		// Получаем фактический порт, назначенный системой
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;

		listener.Stop(); // Освобождаем порт немедленно
		return port;
	}
}

