namespace FsHealth

open System
open System.IO
open System.Security.Cryptography
open System.Threading.Tasks

module Encryption =
    let private header = [| byte 'F'; byte 'S'; byte 'H'; 1uy |]

    let encrypt (key: byte array) (plaintext: byte array) : byte array =
        let nonce = RandomNumberGenerator.GetBytes 12
        let tag = Array.zeroCreate<byte> 16
        let ciphertext = Array.zeroCreate<byte> plaintext.Length
        use aes = new AesGcm(key, 16)
        aes.Encrypt(nonce, plaintext, ciphertext, tag, header)
        Array.concat [ header; nonce; tag; ciphertext ]

    let decrypt (key: byte array) (payload: byte array) : byte array =
        if payload.Length < 32 || payload[0..3] <> header then
            raise (InvalidDataException "Unsupported encrypted journal.")

        let plaintext = Array.zeroCreate<byte>(payload.Length - 32)
        use aes = new AesGcm(key, 16)
        aes.Decrypt(payload.AsSpan(4, 12), payload.AsSpan(32), payload.AsSpan(16, 16), plaintext, header)
        plaintext

module JournalFile =
    let load (path: string) (key: byte array) : Task<Result<Journal, string>> =
        task {
            if not (File.Exists path) then
                return Ok Journal.empty
            else
                let! bytes = File.ReadAllBytesAsync path
                return Encryption.decrypt key bytes |> JournalCodec.decode
        }

    let save (path: string) (key: byte array) (journal: Journal) : Task<unit> =
        task {
            let bytes = JournalCodec.encode journal |> Encryption.encrypt key
            let temporary = path + ".pending"

            try
                do! File.WriteAllBytesAsync(temporary, bytes)
                File.Move(temporary, path, true)
            finally
                if File.Exists temporary then
                    File.Delete temporary
        }
