using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace sndstrm_play
{
    class Program
    {
        // メイン
        static void Main(string[] args)
        {
            CWaveChunkReader wavChunkReader = new CWaveChunkReader();
            BufferedWaveProvider bufferedWaveProvider;


            // 入力ファイル
            Console.WriteLine("input wave file..\n\n");
            string wavFilePath = Console.ReadLine();
            //string wavFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "test.wav");

            // ファイル存在チェック
            if (!File.Exists(wavFilePath))
            {
                throw new FileNotFoundException();
            }

            // ファイル読み込み開始
            using (var _Fs = new FileStream(wavFilePath, FileMode.Open, FileAccess.Read))
            {
                // wavファイルのチャンク読み込み
                wavChunkReader.ReadChunk(_Fs);
                int _rate = wavChunkReader.SamplingRate;
                int _bits = wavChunkReader.SamplingBit;
                int _channels = wavChunkReader.Channels;

                Console.WriteLine($"\n{_bits}[bit] {_rate}[Hz] {_channels}[ch] Loop={wavChunkReader.IsLoop()}");


                // wavフォーマット
                var _wavFormat = new WaveFormat(_rate, _bits, _channels);

                // wavプロバイダーを生成
                bufferedWaveProvider = new BufferedWaveProvider(_wavFormat);

                // ボリューム調整用
                var wavProvider = new VolumeWaveProvider16(bufferedWaveProvider) { Volume = 1.0f };

                // 再生デバイスと出力先を設定(NAudioの用語でRender は出力、Capture は入力)
                var mmDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                // タスクキャンセル用
                var tokenSource = new CancellationTokenSource();
                var token = tokenSource.Token;

                // バッファ監視
                Task _Task = TaskReadBuffer(_Fs, wavChunkReader, bufferedWaveProvider, token);

                // 再生処理
                using (var wavPlayer = new WasapiOut(mmDevice, AudioClientShareMode.Shared, false, 0))
                {
                    // 出力に入力を接続して再生開始
                    wavPlayer.Init(wavProvider);
                    wavPlayer.Volume = 0.2f;
                    wavPlayer.Play();

                    Console.WriteLine("\nPress Button Exit.");
                    Console.ReadLine();

                    wavPlayer.Stop();
                }

                // タスクキャンセル
                tokenSource.Cancel();

            }

        }


        //------------------------------------------------------------------
        // バッファ監視タスク
        static async Task TaskReadBuffer(
            FileStream          _Fs,        // ファイルストリーム
            CWaveChunkReader    _wavChunk,  // wavチャンク
            BufferedWaveProvider _Provider, // バッファプロバイダー
            CancellationToken   _token      // タスクキャンセル通知用
            )
        {
            // 終了位置
            int _StreamEnd = (_wavChunk.IsLoop())? _wavChunk.LoopEnd: _wavChunk.DataEnd;
 
            // データ開始位置へシーク
            _Fs.Seek(_wavChunk.DataStart, SeekOrigin.Begin);

            var _IsLoop = true;
            while (_IsLoop)
            {
                // 空きバッファサイズ（データを追加するサイズ）
                int _EmptySize = _Provider.BufferLength - _Provider.BufferedBytes;
                
                // ストリーム位置チェック
                if (_Fs.Position + _EmptySize > _StreamEnd)
                {
                    _EmptySize = _StreamEnd - (int)_Fs.Position;
                    
                    if (_wavChunk.IsLoop())
                    {
                        // ループ開始位置へ
                        _Fs.Seek(_wavChunk.LoopStart, SeekOrigin.Begin);
                    }
                    else
                    {
                        _IsLoop = false;
                    }
                }

                var _tmp = new byte[_EmptySize];
                if (_EmptySize > 0)
                {
                    // ファイルから読み込み
                    _Fs.Read(_tmp, 0, _EmptySize);
                    // サンプルをバッファへ追加
                    _Provider.AddSamples(_tmp, 0, _EmptySize);
                }
                
                // タスクキャンセル要求チェック
                if (_token.IsCancellationRequested) break;

                await Task.Delay(100);
            }

        }

    }
}
