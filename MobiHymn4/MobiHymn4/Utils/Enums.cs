using System;

namespace MobiHymn4.Utils
{
	public enum InputType
	{
		Grid = 0,
		Numpad = 1
	}

	public enum DownloadStatus
	{
		None = 0,
		Started = 1,
		Ongoing = 2,
		Success = 3,
		Error = 4
	}

	public enum CRUD
	{
		Create = 0,
		Update = 1,
		Delete = 2,
	}

	public enum ResyncType
	{
		Lyrics = 0,
		Audio = 1
	}
}

