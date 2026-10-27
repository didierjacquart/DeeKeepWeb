namespace DeeKeep.Tests.Organize;

/// <summary>Vecteurs produits par le Python d'origine (organize_page.build_plan, dedup_dialog.duplicate_groups et canonical).</summary>
internal static class OrganizeVectors
{
    public const string Json = """
{
 "scenarios": [
  {
   "name": "basic",
   "root": "D:\\Lib",
   "entries": [
    {
     "path": "D:\\Lib\\a.mp3",
     "title": "Africa",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Toto\\Toto IV\\b.mp3",
     "title": "Rosanna",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\c.mp3",
     "title": "Hot2",
     "title_tagged": true,
     "artist": "Inna Feat. Flo Rida",
     "album": "Hot",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\d.mp3",
     "title": "Hot1",
     "title_tagged": true,
     "artist": "Inna",
     "album": "Hot",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\e.mp3",
     "title": "Sans tags",
     "title_tagged": true,
     "artist": "",
     "album": "",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\f.mp3",
     "title": "NoAlbum",
     "title_tagged": true,
     "artist": "AC/DC",
     "album": "",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\g.mp3",
     "title": "September",
     "title_tagged": true,
     "artist": "Earth, Wind & Fire",
     "album": "Best",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    }
   ],
   "options": {},
   "expected": [
    {
     "status": "move",
     "dst": "D:\\Lib\\Toto\\Toto IV\\a.mp3",
     "missing": []
    },
    {
     "status": "inplace",
     "dst": "D:\\Lib\\Toto\\Toto IV\\b.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Inna\\Hot\\c.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Inna\\Hot\\d.mp3",
     "missing": []
    },
    {
     "status": "untagged",
     "dst": "",
     "missing": [
      "artiste",
      "album"
     ]
    },
    {
     "status": "untagged",
     "dst": "",
     "missing": [
      "album"
     ]
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Earth, Wind & Fire\\Best\\g.mp3",
     "missing": []
    }
   ],
   "stats": {
    "move": 4,
    "inplace": 1,
    "untagged": 2,
    "duplicate": 0,
    "divers": 0,
    "from_divers": 0,
    "album": 0,
    "album_dirs": 0
   }
  },
  {
   "name": "rename",
   "root": "D:\\Lib",
   "entries": [
    {
     "path": "D:\\Lib\\x.mp3",
     "title": "Africa",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "3",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\y.mp3",
     "title": "y",
     "title_tagged": false,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "4",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Toto\\Toto IV\\03 - Africa.mp3",
     "title": "Africa",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "3",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    }
   ],
   "options": {
    "rename": true,
    "pattern": "%piste_00% - %titre%"
   },
   "expected": [
    {
     "status": "move",
     "dst": "D:\\Lib\\Toto\\Toto IV\\03 - Africa.mp3",
     "missing": []
    },
    {
     "status": "untagged",
     "dst": "",
     "missing": [
      "titre"
     ]
    },
    {
     "status": "duplicate",
     "dst": "D:\\Lib\\x.mp3",
     "missing": []
    }
   ],
   "stats": {
    "move": 1,
    "inplace": 0,
    "untagged": 1,
    "duplicate": 1,
    "divers": 0,
    "from_divers": 0,
    "album": 0,
    "album_dirs": 0
   }
  },
  {
   "name": "divers",
   "root": "D:\\Lib",
   "entries": [
    {
     "path": "D:\\Lib\\a.mp3",
     "title": "T1",
     "title_tagged": true,
     "artist": "Solo",
     "album": "A",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\b.mp3",
     "title": "T2",
     "title_tagged": true,
     "artist": "Duo",
     "album": "A",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\c.mp3",
     "title": "T3",
     "title_tagged": true,
     "artist": "Duo",
     "album": "B",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\d.mp3",
     "title": "T4",
     "title_tagged": true,
     "artist": "Trio",
     "album": "A",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\e.mp3",
     "title": "T5",
     "title_tagged": true,
     "artist": "Trio",
     "album": "A",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\f.mp3",
     "title": "T6",
     "title_tagged": true,
     "artist": "Trio",
     "album": "A",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Divers\\g.mp3",
     "title": "T7",
     "title_tagged": true,
     "artist": "Trio",
     "album": "A",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Divers\\h.mp3",
     "title": "T8",
     "title_tagged": true,
     "artist": "Solo",
     "album": "",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    }
   ],
   "options": {
    "divers": true,
    "divers_min": 3
   },
   "expected": [
    {
     "status": "move",
     "dst": "D:\\Lib\\Divers\\a.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Divers\\b.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Divers\\c.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Trio\\A\\d.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Trio\\A\\e.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Trio\\A\\f.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Trio\\A\\g.mp3",
     "missing": []
    },
    {
     "status": "inplace",
     "dst": "D:\\Lib\\Divers\\h.mp3",
     "missing": []
    }
   ],
   "stats": {
    "move": 7,
    "inplace": 1,
    "untagged": 0,
    "duplicate": 0,
    "divers": 3,
    "from_divers": 1,
    "album": 0,
    "album_dirs": 0
   }
  },
  {
   "name": "albums",
   "root": "D:\\Lib",
   "entries": [
    {
     "path": "D:\\Lib\\Footloose\\1.mp3",
     "title": "S1",
     "title_tagged": true,
     "artist": "A1",
     "album": "Footloose (Original Motion Picture Soundtrack)",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Footloose\\2.mp3",
     "title": "S2",
     "title_tagged": true,
     "artist": "A2",
     "album": "Footloose (Original Motion Picture Soundtrack)",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Zen 2\\1.mp3",
     "title": "Z1",
     "title_tagged": true,
     "artist": "B1",
     "album": "Zen 2",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Zen 2\\2.mp3",
     "title": "Z2",
     "title_tagged": true,
     "artist": "B2",
     "album": "Zen 2",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Zen 2\\3.mp3",
     "title": "Z3",
     "title_tagged": true,
     "artist": "B3",
     "album": "Zen 2",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Best of Blondie\\1.mp3",
     "title": "P1",
     "title_tagged": true,
     "artist": "Blondie",
     "album": "Best of Blondie",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Best of Blondie\\2.mp3",
     "title": "P2",
     "title_tagged": true,
     "artist": "Blondie",
     "album": "Best of Blondie",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Best of Blondie\\3.mp3",
     "title": "P3",
     "title_tagged": true,
     "artist": "Blondie Feat. X",
     "album": "Best of Blondie",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Mix\\1.mp3",
     "title": "M1",
     "title_tagged": true,
     "artist": "C1",
     "album": "Other",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    }
   ],
   "options": {},
   "expected": [
    {
     "status": "album",
     "dst": "",
     "missing": []
    },
    {
     "status": "album",
     "dst": "",
     "missing": []
    },
    {
     "status": "album",
     "dst": "",
     "missing": []
    },
    {
     "status": "album",
     "dst": "",
     "missing": []
    },
    {
     "status": "album",
     "dst": "",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Blondie\\Best of Blondie\\1.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Blondie\\Best of Blondie\\2.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Blondie\\Best of Blondie\\3.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\C1\\Other\\1.mp3",
     "missing": []
    }
   ],
   "stats": {
    "move": 4,
    "inplace": 0,
    "untagged": 0,
    "duplicate": 0,
    "divers": 0,
    "from_divers": 0,
    "album": 5,
    "album_dirs": 2
   }
  },
  {
   "name": "dedup",
   "root": "D:\\Lib",
   "entries": [
    {
     "path": "D:\\Lib\\Toto\\Toto IV\\Africa.mp3",
     "title": "Africa",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\Divers\\Africa.mp3",
     "title": "Africa",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\x\\y\\Africa.mp3",
     "title": "Africa",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    },
    {
     "path": "D:\\Lib\\z\\Rosanna.mp3",
     "title": "Rosanna",
     "title_tagged": true,
     "artist": "Toto",
     "album": "Toto IV",
     "albumartist": "",
     "genre": "",
     "composer": "",
     "publisher": "",
     "comment": "",
     "compilation": "",
     "track": "",
     "tracktotal": "",
     "disc": "",
     "disctotal": "",
     "date": "",
     "bitrate": 0,
     "format": "MP3"
    }
   ],
   "options": {
    "divers": true,
    "divers_min": 3
   },
   "expected": [
    {
     "status": "duplicate",
     "dst": "D:\\Lib\\x\\y\\Africa.mp3",
     "missing": []
    },
    {
     "status": "duplicate",
     "dst": "D:\\Lib\\x\\y\\Africa.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Divers\\Africa.mp3",
     "missing": []
    },
    {
     "status": "move",
     "dst": "D:\\Lib\\Divers\\Rosanna.mp3",
     "missing": []
    }
   ],
   "stats": {
    "move": 2,
    "inplace": 0,
    "untagged": 0,
    "duplicate": 2,
    "divers": 2,
    "from_divers": 0,
    "album": 0,
    "album_dirs": 0
   }
  }
 ],
 "dupEntries": [
  {
   "path": "D:\\Lib\\A\\Song One.mp3",
   "title": "Song One",
   "title_tagged": true,
   "artist": "Artist",
   "album": "",
   "albumartist": "",
   "genre": "",
   "composer": "",
   "publisher": "",
   "comment": "",
   "compilation": "",
   "track": "",
   "tracktotal": "",
   "disc": "",
   "disctotal": "",
   "date": "",
   "bitrate": 0,
   "format": "MP3"
  },
  {
   "path": "D:\\Lib\\B\\01 - Song One (copy).mp3",
   "title": "x",
   "title_tagged": true,
   "artist": "Other",
   "album": "",
   "albumartist": "",
   "genre": "",
   "composer": "",
   "publisher": "",
   "comment": "",
   "compilation": "",
   "track": "",
   "tracktotal": "",
   "disc": "",
   "disctotal": "",
   "date": "",
   "bitrate": 0,
   "format": "MP3"
  },
  {
   "path": "D:\\Lib\\C\\z.mp3",
   "title": "Song One",
   "title_tagged": true,
   "artist": "Artist",
   "album": "",
   "albumartist": "",
   "genre": "",
   "composer": "",
   "publisher": "",
   "comment": "",
   "compilation": "",
   "track": "",
   "tracktotal": "",
   "disc": "",
   "disctotal": "",
   "date": "",
   "bitrate": 0,
   "format": "MP3"
  },
  {
   "path": "D:\\Lib\\D\\Felix Jaehn - Ain't Nobody.mp3",
   "title": "Ain't Nobody",
   "title_tagged": true,
   "artist": "Felix Jaehn Feat. Jasmine",
   "album": "",
   "albumartist": "",
   "genre": "",
   "composer": "",
   "publisher": "",
   "comment": "",
   "compilation": "",
   "track": "",
   "tracktotal": "",
   "disc": "",
   "disctotal": "",
   "date": "",
   "bitrate": 0,
   "format": "MP3"
  },
  {
   "path": "D:\\Lib\\E\\q.mp3",
   "title": "Ain't Nobody",
   "title_tagged": true,
   "artist": "Felix Jaehn",
   "album": "",
   "albumartist": "",
   "genre": "",
   "composer": "",
   "publisher": "",
   "comment": "",
   "compilation": "",
   "track": "",
   "tracktotal": "",
   "disc": "",
   "disctotal": "",
   "date": "",
   "bitrate": 0,
   "format": "MP3"
  },
  {
   "path": "D:\\Lib\\F\\Alone.mp3",
   "title": "Alone",
   "title_tagged": true,
   "artist": "Zed",
   "album": "",
   "albumartist": "",
   "genre": "",
   "composer": "",
   "publisher": "",
   "comment": "",
   "compilation": "",
   "track": "",
   "tracktotal": "",
   "disc": "",
   "disctotal": "",
   "date": "",
   "bitrate": 0,
   "format": "MP3"
  }
 ],
 "dupGroups": [
  [
   0,
   1,
   2
  ],
  [
   3,
   4
  ]
 ],
 "dupKeys": [
  [
   "a:artist|song one",
   "f:song one",
   "m:artist|song one"
  ],
  [
   "a:other|x",
   "f:song one",
   "m:other|x"
  ],
  [
   "a:artist|song one",
   "f:z",
   "m:artist|song one"
  ],
  [
   "a:felix jaehn|ain t nobody",
   "f:felix jaehn ain t nobody",
   "m:felix jaehn feat jasmine|ain t nobody"
  ],
  [
   "a:felix jaehn|ain t nobody",
   "f:q",
   "m:felix jaehn|ain t nobody"
  ],
  [
   "a:zed|alone",
   "f:alone",
   "m:zed|alone"
  ]
 ],
 "canon": {
  "Été 2020": "t 2020",
  "Song (1) copy": "song",
  "03 - Africa": "africa",
  "Ke$ha - Tik Tok": "ke ha tik tok",
  "A  B--C": "a b c"
 }
}
""";
}
