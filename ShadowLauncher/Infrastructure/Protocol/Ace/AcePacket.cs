using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.Protocol.Ace;

internal enum AcePacketHeaderFlags : uint
{
    BlobFragments = 0x00000004,
    AckSequence = 0x00004000,
    Disconnect = 0x00008000,
    LoginRequest = 0x00010000,
    ConnectRequest = 0x00040000,
    ConnectResponse = 0x00080000,
    EncryptedChecksum = 0x00000002,
}

internal enum AceGameMessageOpcode : uint
{
    AccountBoot = 0xF7DC,
    DDD_Interrogation = 0xF7E5,
    DDD_InterrogationResponse = 0xF7E6,
    DDD_BeginDDD = 0xF7E7,
    DDD_EndDDD = 0xF7EA,
}

internal static class AceDatIterationReader
{
    private const int GamePackVersionOffset = 0x178;

    public static int? TryReadGamePackVersion(string datFilePath)
    {
        if (!File.Exists(datFilePath))
            return null;

        try
        {
            using var fs = File.OpenRead(datFilePath);
            if (fs.Length < GamePackVersionOffset + 4)
                return null;

            Span<byte> buf = stackalloc byte[4];
            fs.Seek(GamePackVersionOffset, SeekOrigin.Begin);
            return fs.Read(buf) == 4
                ? (int)BinaryPrimitives.ReadUInt32LittleEndian(buf)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static AceDatIterations ReadFromCacheDir(string cacheDir) => new(
        TryReadGamePackVersion(Path.Combine(cacheDir, "client_portal.dat")),
        TryReadGamePackVersion(Path.Combine(cacheDir, "client_cell_1.dat")),
        TryReadGamePackVersion(Path.Combine(cacheDir, "client_local_English.dat")),
        File.Exists(Path.Combine(cacheDir, "client_highres.dat"))
            ? TryReadGamePackVersion(Path.Combine(cacheDir, "client_highres.dat"))
            : null);
}

internal readonly record struct AceDatIterations(int? Portal, int? Cell, int? Language, int? HighRes);

internal sealed class AceDatDddProbe
{
    private const int HeaderSize = 20;
    private const int FragmentHeaderSize = 16;
    private const int MaxFragmentData = 448;
    private const int HiFiDatFileType = unchecked((int)0x69466948);

    private readonly ILogger _logger;

    public AceDatDddProbe(ILogger logger) => _logger = logger;

    public async Task<AceDatProbeOutcome> ProbeAsync(
        Server server,
        Account account,
        string datCacheDir,
        CancellationToken cancellationToken)
    {
        if (server.SecureLogon)
        {
            _logger.LogDebug("Skipping DDD probe for '{Server}': secure logon uses GLS ticket", server.Name);
            return AceDatProbeOutcome.Inconclusive;
        }

        var iterations = AceDatIterationReader.ReadFromCacheDir(datCacheDir);
        if (iterations.Portal is null or <= 0)
        {
            _logger.LogWarning("Cannot probe '{Server}': portal iteration missing in cache", server.Name);
            return AceDatProbeOutcome.Inconclusive;
        }

        IPAddress address;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(server.Hostname, cancellationToken);
            address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                      ?? addresses[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DDD probe DNS failed for '{Server}'", server.Name);
            return AceDatProbeOutcome.Inconclusive;
        }

        using var udp = new UdpClient(0);
        var serverEp = new IPEndPoint(address, server.Port);
        var serverEpPlusOne = new IPEndPoint(address, server.Port + 1);

        var session = new ProbeSession();
        var outcome = AceDatProbeOutcome.Inconclusive;
        var sentDddResponse = false;
        var deadline = DateTime.UtcNow.AddSeconds(15);

        try
        {
            var login = BuildLoginPacket(account.Name, account.Password);
            await udp.SendAsync(login, login.Length, serverEp);

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var waitMs = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                recvCts.CancelAfter(Math.Min(waitMs, 2000));

                byte[] datagram;
                try
                {
                    var result = await udp.ReceiveAsync(recvCts.Token);
                    datagram = result.Buffer;
                }
                catch (OperationCanceledException)
                {
                    continue;
                }

                if (!TryParsePacket(datagram, session, out var body, out var opcodes))
                    continue;

                if ((session.Flags & AcePacketHeaderFlags.ConnectRequest) != 0 && !session.HandshakeComplete)
                {
                    if (TryParseConnectRequest(body, session))
                    {
                        session.OutboundCrypto = new AceCryptoSystem(session.ClientSeedBytes);
                        session.InboundCrypto = new AceCryptoSystem(session.ServerSeedBytes);
                        var connectResponse = BuildConnectResponsePacket(session.Cookie);
                        await udp.SendAsync(connectResponse, connectResponse.Length, serverEpPlusOne);
                        session.HandshakeComplete = true;
                        session.SendSequence = 1;
                    }

                    continue;
                }

                foreach (var opcode in opcodes)
                {
                    switch (opcode)
                    {
                        case AceGameMessageOpcode.DDD_Interrogation when !sentDddResponse:
                        {
                            var response = BuildDddInterrogationResponsePacket(session, iterations);
                            await udp.SendAsync(response, response.Length, serverEp);
                            sentDddResponse = true;
                            break;
                        }
                        case AceGameMessageOpcode.DDD_BeginDDD:
                        case AceGameMessageOpcode.AccountBoot when sentDddResponse:
                            outcome = AceDatProbeOutcome.Mismatch;
                            deadline = DateTime.UtcNow;
                            break;
                        case AceGameMessageOpcode.DDD_EndDDD when sentDddResponse:
                            outcome = AceDatProbeOutcome.Match;
                            deadline = DateTime.UtcNow;
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DDD probe error for '{Server}'", server.Name);
            outcome = AceDatProbeOutcome.Inconclusive;
        }
        finally
        {
            await SendDisconnectAsync(udp, session, serverEp, cancellationToken);
            await Task.Delay(200, cancellationToken);
        }

        _logger.LogInformation("DDD probe for '{Server}' result: {Outcome}", server.Name, outcome);
        return outcome;
    }

    private async Task SendDisconnectAsync(
        UdpClient udp,
        ProbeSession session,
        IPEndPoint serverEp,
        CancellationToken cancellationToken)
    {
        if (!session.HandshakeComplete)
            return;

        try
        {
            var packet = BuildSimplePacket(
                session,
                AcePacketHeaderFlags.Disconnect,
                ReadOnlySpan<byte>.Empty,
                encrypt: true);
            await udp.SendAsync(packet, packet.Length, serverEp);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DDD probe disconnect send failed (non-fatal)");
        }
    }

    private bool TryParsePacket(byte[] datagram, ProbeSession session, out ReadOnlySpan<byte> body, out List<AceGameMessageOpcode> opcodes)
    {
        body = ReadOnlySpan<byte>.Empty;
        opcodes = [];

        if (datagram.Length < HeaderSize)
            return false;

        var flags = (AcePacketHeaderFlags)BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4));
        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(8));
        var id = BinaryPrimitives.ReadUInt16LittleEndian(datagram.AsSpan(12));
        var size = BinaryPrimitives.ReadUInt16LittleEndian(datagram.AsSpan(16));

        if (datagram.Length < HeaderSize + size)
            return false;

        session.Flags = flags;
        if (id != 0)
            session.ClientId = id;

        var payload = datagram.AsSpan(HeaderSize, size);
        body = payload;

        if ((flags & AcePacketHeaderFlags.ConnectRequest) != 0)
            return true;

        if ((flags & AcePacketHeaderFlags.BlobFragments) == 0)
        {
            CollectOpcodes(payload, opcodes);
            return true;
        }

        if (!VerifyChecksum(datagram.AsSpan(0, HeaderSize + size), flags, checksum, session.InboundCrypto))
            return false;

        var offset = 0;
        while (offset + FragmentHeaderSize <= payload.Length)
        {
            var fragSize = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset + 10));
            if (fragSize < FragmentHeaderSize || offset + fragSize > payload.Length)
                break;

            var fragData = payload.Slice(offset + FragmentHeaderSize, fragSize - FragmentHeaderSize);
            CollectOpcodes(fragData, opcodes);
            offset += fragSize;
        }

        return true;
    }

    private static void CollectOpcodes(ReadOnlySpan<byte> data, List<AceGameMessageOpcode> opcodes)
    {
        if (data.Length < 4)
            return;

        var opcode = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (Enum.IsDefined(typeof(AceGameMessageOpcode), opcode))
            opcodes.Add((AceGameMessageOpcode)opcode);
    }

    private static bool VerifyChecksum(
        ReadOnlySpan<byte> packet,
        AcePacketHeaderFlags flags,
        uint checksum,
        AceCryptoSystem? inboundCrypto)
    {
        if ((flags & AcePacketHeaderFlags.EncryptedChecksum) == 0)
            return true;

        if (inboundCrypto is null)
            return false;

        var headerCopy = packet.Slice(0, HeaderSize).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(headerCopy.AsSpan(8), 0xBADD70DD);
        var headerHash = AceHash32.Calculate(headerCopy.AsSpan(0, HeaderSize));

        var payload = packet.Slice(HeaderSize);
        var payloadHash = AceHash32.Calculate(payload);
        var key = (checksum - headerHash) ^ payloadHash;
        return inboundCrypto.Search(key);
    }

    private static bool TryParseConnectRequest(ReadOnlySpan<byte> body, ProbeSession session)
    {
        if (body.Length < 32)
            return false;

        session.Cookie = BinaryPrimitives.ReadUInt64LittleEndian(body.Slice(8));
        session.ClientId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(16));
        session.ServerSeedBytes = body.Slice(20, 4).ToArray();
        session.ClientSeedBytes = body.Slice(24, 4).ToArray();
        return true;
    }

    private static byte[] BuildLoginPacket(string account, string password)
    {
        using var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload))
        {
            WriteString16L(w, "1802");
            var bodyStart = payload.Length;
            w.Write(0u); // placeholder length
            w.Write(2u); // NetAuthType AccountPassword
            w.Write(0u); // auth flags
            w.Write((uint)Environment.TickCount);
            WriteString16L(w, account);
            WriteString16L(w, string.Empty);
            WriteString32L(w, password);

            var bodyLen = (int)(payload.Length - bodyStart - sizeof(uint));
            payload.Position = bodyStart;
            w.Write((uint)bodyLen);
        }

        var payloadBytes = payload.ToArray();
        return BuildCleartextPacket(0, AcePacketHeaderFlags.LoginRequest, payloadBytes);
    }

    private static byte[] BuildConnectResponsePacket(ulong cookie)
    {
        Span<byte> cookieBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(cookieBytes, cookie);
        return BuildCleartextPacket(0, AcePacketHeaderFlags.ConnectResponse, cookieBytes);
    }

    private byte[] BuildDddInterrogationResponsePacket(ProbeSession session, AceDatIterations iterations)
    {
        using var msg = new MemoryStream();
        using (var w = new BinaryWriter(msg))
        {
            w.Write((uint)AceGameMessageOpcode.DDD_InterrogationResponse);
            w.Write(1u); // client language
            WriteIterationList(w, iterations);
        }

        return BuildSimplePacket(session, AcePacketHeaderFlags.BlobFragments, msg.ToArray(), encrypt: true);
    }

    private static void WriteIterationList(BinaryWriter w, AceDatIterations iterations)
    {
        var entries = new List<(int DatFileId, int DatFileType, int Iteration)>();
        if (iterations.Portal is int p)
            entries.Add((1, 0, p));
        if (iterations.Cell is int c)
            entries.Add((2, 0, c));
        if (iterations.Language is int l)
            entries.Add((3, 0, l));
        if (iterations.HighRes is int h)
            entries.Add((1, HiFiDatFileType, h));

        w.Write(entries.Count);
        foreach (var (datFileId, datFileType, iteration) in entries)
        {
            w.Write(datFileType);
            w.Write(datFileId);
            w.Write(iteration);
            w.Write(-(iteration + 1));
        }
    }

    private static byte[] BuildSimplePacket(
        ProbeSession session,
        AcePacketHeaderFlags flags,
        ReadOnlySpan<byte> messageBody,
        bool encrypt)
    {
        using var payload = new MemoryStream();
        if (!messageBody.IsEmpty)
        {
            var fragHeader = new byte[FragmentHeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(fragHeader.AsSpan(0), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(fragHeader.AsSpan(4), 0x80000000);
            BinaryPrimitives.WriteUInt16LittleEndian(fragHeader.AsSpan(8), 1);
            var fragSize = (ushort)(FragmentHeaderSize + messageBody.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(fragHeader.AsSpan(10), fragSize);
            BinaryPrimitives.WriteUInt16LittleEndian(fragHeader.AsSpan(12), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(fragHeader.AsSpan(14), 5); // DatabaseQueue

            payload.Write(fragHeader);
            payload.Write(messageBody);
        }

        var payloadBytes = payload.ToArray();
        flags |= AcePacketHeaderFlags.BlobFragments;
        if (encrypt)
            flags |= AcePacketHeaderFlags.EncryptedChecksum;

        var seq = session.SendSequence++;
        return BuildPacket(seq, session.ClientId, flags, payloadBytes, session.OutboundCrypto);
    }

    private static byte[] BuildCleartextPacket(uint sequence, AcePacketHeaderFlags flags, ReadOnlySpan<byte> payload)
        => BuildPacket(sequence, 0, flags, payload, null);

    private static byte[] BuildPacket(
        uint sequence,
        uint id,
        AcePacketHeaderFlags flags,
        ReadOnlySpan<byte> payload,
        AceCryptoSystem? outboundCrypto)
    {
        var packet = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)flags);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), (ushort)id);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16), (ushort)payload.Length);

        if (!payload.IsEmpty)
            payload.CopyTo(packet.AsSpan(HeaderSize));

        var headerForHash = packet.AsSpan(0, HeaderSize).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(headerForHash.AsSpan(8), 0xBADD70DD);
        var headerHash = AceHash32.Calculate(headerForHash);

        var payloadHash = payload.IsEmpty ? 0u : AceHash32.Calculate(payload);

        uint checksum;
        if ((flags & AcePacketHeaderFlags.EncryptedChecksum) != 0 && outboundCrypto is not null)
        {
            var xor = outboundCrypto.Next();
            checksum = headerHash + (payloadHash ^ xor);
        }
        else
        {
            checksum = headerHash + payloadHash;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), checksum);
        return packet;
    }

    private static void WriteString16L(BinaryWriter w, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
        var pad = (4 - ((2 + bytes.Length) % 4)) % 4;
        for (var i = 0; i < pad; i++)
            w.Write((byte)0);
    }

    private static void WriteString32L(BinaryWriter w, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var prefixSize = bytes.Length <= 254 ? 2 : 3;
        var contentLen = prefixSize + bytes.Length;
        var pad = (4 - (contentLen % 4)) % 4;
        w.Write((uint)(4 + contentLen + pad));
        w.Write((byte)1);
        if (bytes.Length <= 254)
            w.Write((byte)bytes.Length);
        else
        {
            w.Write((byte)0);
            w.Write((byte)bytes.Length);
        }

        w.Write(bytes);
        for (var i = 0; i < pad; i++)
            w.Write((byte)0);
    }

    private sealed class ProbeSession
    {
        public AcePacketHeaderFlags Flags;
        public uint ClientId;
        public ulong Cookie;
        public byte[] ServerSeedBytes = [];
        public byte[] ClientSeedBytes = [];
        public AceCryptoSystem? OutboundCrypto;
        public AceCryptoSystem? InboundCrypto;
        public uint SendSequence;
        public bool HandshakeComplete;
    }
}
