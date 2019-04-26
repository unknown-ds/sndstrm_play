using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace sndstrm_play
{
    //======================================================================
    //----------------------------------------------------------------------
    // wavファイルのチャンクデータを取得クラス
    public class CWaveChunkReader
    {
        // 入力ファイル名
        public string WaveFileName { set; get; }
        // 走査済サイズ
        public UInt32 ReadedSize { private set; get; } = 0;

        // チャンネル数
        public Int16 Channels { private set; get; } 
        // サンプリングレート（Hz）
        public Int32 SamplingRate { private set; get; }
        // 量子化ビット数
        public Int32 SamplingBit { private set; get; }
        // サンプル数
        public long SampleNum { private set; get; }
        // サンプルフレーム数
        public long NumSampleFrames { private set; get; }

        // チャンク情報
        public class CChunkInfo
        {
            public string Id { set; get; }      // チャンクID
            public UInt32 Size { set; get; }     // サイズ
        }
        public List<CChunkInfo> ChunkInfoList { set; get; }

        // データ情報
        public Int32 DataStart { private set; get; }
        public Int32 DataEnd { private set; get; }
        public Int32 DataSize { private set; get; }

        // ループ情報
        public class CLoopInfo
        {
            public Int32 dwIdentifier { set; get; }    // ID
            public Int32 dwType { set; get; }          // ループタイプ
            public Int32 dwStart { set; get; }         // ループ開始位置
            public Int32 dwEnd { set; get; }           // ループ終了位置
            public Int32 dwFraction { set; get; }      // ループチューニング用
            public Int32 dwPlayCount { set; get; }     // ループ回数(0=無限)
        }
        public List<CLoopInfo> LoopInfoList { set; get; }

        // ループ情報があるか？
        public bool IsLoop() => (LoopInfoList?.Count == 0) ? false : true;

        // ループ開始位置と終了位置
        public Int32 LoopStart { private set; get; }
        public Int32 LoopEnd { private set; get; }
 

        // プレイ時間(sec)取得
        public float GetAllPlaySec() => (float)NumSampleFrames / (float)SamplingRate;


        //=======================================
        //---------------------------------------
        // コンストラクタ
        public CWaveChunkReader()
        {
            WaveFileName = null;
            ChunkInfoList = new List<CChunkInfo>();
            LoopInfoList = new List<CLoopInfo>();
        }


        //---------------------------------------
        // チャンク読み込み
        public void ReadChunk(
            Stream      _Stream      // ストリーム
            )
        {
            string _CkID = null;
            UInt32 _CkSize = 0;

            // バイナリ読み込み
            var _Br = new BinaryReader(_Stream);
            {
                // Riffチャンクチェック
                readChunk(_Br, ref _CkID, ref _CkSize);
                char[] _FormType = _Br.ReadChars(4);

                if ((_CkID != "RIFF") || string.Join("", _FormType) != "WAVE")
                {
                    throw new ApplicationException();
                }

                // チャンクデータ読み込み
                UInt32 _FormChunkSize = _CkSize;
                ReadedSize = 12;
                while (ReadedSize < _FormChunkSize)
                {
                    ReadedSize += readChunk(_Br, ref _CkID, ref _CkSize);

                    switch (_CkID.ToUpper())
                    {
                    case "FMT ": { readFormatChunk(_Br, _CkID, _CkSize); } break;
                    case "DATA": { readDataChunk(_Br, _CkID, _CkSize); } break;
                    case "SMPL": { readSmplChunk(_Br, _CkID, _CkSize); } break;
                    default:
                    {
                        // 読み飛ばし
                        _Stream.Seek(_CkSize, SeekOrigin.Current);
                    }
                    break;
                    }

                    // チャンク情報保持
                    ChunkInfoList.Add(new CChunkInfo() { Id = _CkID.ToString(), Size = _CkSize });
                    // 走査済サイズを加算
                    ReadedSize += _CkSize;
                }
            }

            // ストリーム位置リセット
            _Stream.Seek(0, SeekOrigin.Begin);
        }


        //---------------------------------------
        // チャンクを読む
        protected UInt32 readChunk(
            BinaryReader _Br,       // バイナリリーダー
            ref string _Chunk,      // チャンク文字
            ref UInt32 _Size        // サイズ
            )
        {
            char[] _Ck = _Br.ReadChars(4);
            _Chunk = string.Join("", _Ck);
            _Size = _Br.ReadUInt32();

            return (8);
        }


        //---------------------------------------
        // FMTチャンク解析
        protected UInt32 readFormatChunk(
            BinaryReader _Br,       // バイナリリーダー
            string _ChunkId,        // "FMT "
            UInt32 _ChunkSize       // リニアPCなら必ず16
            )
        {
            // フォーマットタグ、無圧縮なら1
            var _wFormatTag = _Br.ReadUInt16();
            // チャンネル数
            var _wChannels = _Br.ReadUInt16();
            // サンプリングレート（Hz）
            var dwSamplesPerSec = _Br.ReadUInt32();
            // データ速度(byte/sec)
            var _dwAvgBytesPerSec = _Br.ReadUInt32();
            // ブロックサイズ（Byte/sample×チャンネル数）
            var _wBlockAlign = _Br.ReadUInt16();
            // サンプルあたりのbit数(bit/sample) 8bit|16bir
            var _wBitsPerSample = _Br.ReadUInt16();

            UInt32 _ReadSize = 16;

            Channels = (Int16)_wChannels;
            SamplingBit = _wBitsPerSample;
            SamplingRate = (Int32)dwSamplesPerSec;


            // 読み出しサイズチェック
            if (_ReadSize != _ChunkSize)
            {
                throw new ApplicationException();
            }

            return (_ReadSize);
        }


        //---------------------------------------
        // DATAチャンク解析
        protected UInt32 readDataChunk(
            BinaryReader _Br,       // バイナリリーダー
            string _ChunkId,        // "DATA"
            UInt32 _ChunkSize       // 不定
            )
        {
            var _SampleNum = _ChunkSize / (SamplingBit / 8);
            var _NumSampleFrames = _SampleNum / Channels;

            // サンプル数
            SampleNum = _SampleNum;
            // サンプルフレーム数
            NumSampleFrames = _NumSampleFrames;

            _Br.BaseStream.Seek(_ChunkSize, SeekOrigin.Current);

            // データチャンク情報セット
            DataStart = (int)ReadedSize;
            DataSize = (int)_ChunkSize;
            DataEnd = DataStart + DataSize;

            return (_ChunkSize);
        }


        //---------------------------------------
        // SMPLチャンク解析
        protected UInt32 readSmplChunk(
            BinaryReader _Br,       // バイナリリーダー
            string _ChunkId,        // "SMPL"
            UInt32 _ChunkSize       // 不定
            )
        {
            Int32 _ReadByte = 0;

            var dwManufacturer = _Br.ReadUInt32();
            var dwProduct = _Br.ReadUInt32();
            var dwSamplePeriod = _Br.ReadUInt32();      // 1サンプルの時間[nsec]
            var dwMIDIUnityNote = _Br.ReadUInt32();
            var dwMIDIPitchFraction = _Br.ReadUInt32();
            var dwSMPTEFormat = _Br.ReadUInt32();
            var dwSMPTEOffset = _Br.ReadUInt32();
            var cSampleLoops = _Br.ReadUInt32();        // ループ情報数
            var cbSamplerData = _Br.ReadUInt32();

            _ReadByte += (4 * 9);

            for (Int32 _Lp = 0; _Lp < cSampleLoops; _Lp++)
            {
                var dwIdentifier = _Br.ReadUInt32();    // ID
                var dwType = _Br.ReadUInt32();          // ループタイプ
                var dwStart = _Br.ReadUInt32();         // ループ開始位置
                var dwEnd = _Br.ReadUInt32();           // ループ終了位置
                var dwFraction = _Br.ReadUInt32();      // ループチューニング用
                var dwPlayCount = _Br.ReadUInt32();     // ループ回数(0=無限)

                LoopInfoList.Add(new CLoopInfo()
                {
                    dwIdentifier = (int)dwIdentifier,
                    dwType = (int)dwType,
                    dwStart = (int)dwStart,
                    dwEnd = (int)dwEnd,
                    dwFraction = (int)dwFraction,
                    dwPlayCount = (int)dwPlayCount
                });

                _ReadByte += (4 * 6);
            }

            // 1サンプル当たりのバイト数
            var _SampleByte = (SamplingBit / 8) * Channels;
            // ループ位置計算（複数ループは見ていない）
            if (IsLoop())
            {
                LoopStart = LoopInfoList[0].dwStart * _SampleByte + DataStart;
                LoopEnd = LoopInfoList[0].dwEnd * _SampleByte + DataStart;
            }

            // 読み出しサイズチェック
            if (_ReadByte != _ChunkSize)
            {
                throw new ApplicationException();
            }

            return (_ChunkSize);
        }


    }
}
