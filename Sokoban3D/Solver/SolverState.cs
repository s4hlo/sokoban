using System;
using Sokoban3D.Core;

namespace Sokoban3D.Solver;

/// <summary>
/// Snapshot canônico e hashável do mundo pra busca: posição + solidez de cada peça móvel
/// (ordem estável definida pelo <see cref="SolverSim"/>), o olhar do player, o estado caído e —
/// só no tier atemporal — as pilhas do histórico por peça. Estado DERIVADO fica fora de
/// propósito: a solidez dos toggles e o Y "de queda" re-derivam das posições, então dois
/// snapshots iguais aqui são o mesmo estado de jogo. Imutável após criado; o hash é
/// pré-computado no construtor (cada estado é consultado muitas vezes no visited-set).
/// </summary>
public sealed class SolverState : IEquatable<SolverState>
{
    /// <summary>Posição + solidez de uma peça móvel (solidez falsa = frágil quebrada).</summary>
    public readonly struct Piece
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;
        public readonly bool Solid;

        public Piece(int x, int y, int z, bool solid)
        {
            X = x;
            Y = y;
            Z = z;
            Solid = solid;
        }
    }

    public readonly Piece[] Pieces;
    public readonly int FacingDx;
    public readonly int FacingDz;
    public readonly bool PlayerFell;

    /// <summary>
    /// Pilhas do histórico por peça (mesmo índice de <see cref="Pieces"/>, fundo → topo).
    /// Null no tier sem atemporal — lá o undo não é ação e o passado não diferencia futuros.
    /// Arrays vazios pra peças sem pilha (forma canônica).
    /// </summary>
    public readonly Move[][] Stacks;

    private readonly int _hash;

    public SolverState(Piece[] pieces, int facingDx, int facingDz, bool playerFell, Move[][] stacks)
    {
        Pieces = pieces;
        FacingDx = facingDx;
        FacingDz = facingDz;
        PlayerFell = playerFell;
        Stacks = stacks;
        _hash = ComputeHash();
    }

    public bool Equals(SolverState other)
    {
        if (other is null || _hash != other._hash)
            return false;
        if (FacingDx != other.FacingDx || FacingDz != other.FacingDz || PlayerFell != other.PlayerFell)
            return false;
        if (Pieces.Length != other.Pieces.Length)
            return false;

        for (int i = 0; i < Pieces.Length; i++)
        {
            var a = Pieces[i];
            var b = other.Pieces[i];
            if (a.X != b.X || a.Y != b.Y || a.Z != b.Z || a.Solid != b.Solid)
                return false;
        }

        if (Stacks is null != other.Stacks is null)
            return false;
        if (Stacks is null)
            return true;

        for (int i = 0; i < Stacks.Length; i++)
        {
            var sa = Stacks[i];
            var sb = other.Stacks[i];
            if (sa.Length != sb.Length)
                return false;
            for (int j = 0; j < sa.Length; j++)
            {
                var ma = sa[j];
                var mb = sb[j];
                if (ma.Dx != mb.Dx || ma.Dy != mb.Dy || ma.Dz != mb.Dz || ma.Solid != mb.Solid)
                    return false;
                // O olhar gravado no turno também é passado que o undo devolve: entra na chave.
                if (ma.Face.HasValue != mb.Face.HasValue)
                    return false;
                if (ma.Face.HasValue
                    && (ma.Face.Value.Dx != mb.Face.Value.Dx || ma.Face.Value.Dz != mb.Face.Value.Dz))
                    return false;
            }
        }
        return true;
    }

    public override bool Equals(object obj) => Equals(obj as SolverState);

    public override int GetHashCode() => _hash;

    private int ComputeHash()
    {
        var hash = new HashCode();
        hash.Add(FacingDx);
        hash.Add(FacingDz);
        hash.Add(PlayerFell);
        foreach (var p in Pieces)
        {
            hash.Add(p.X);
            hash.Add(p.Y);
            hash.Add(p.Z);
            hash.Add(p.Solid);
        }
        if (Stacks is not null)
        {
            foreach (var stack in Stacks)
            {
                hash.Add(stack.Length);
                foreach (var m in stack)
                {
                    hash.Add(m.Dx);
                    hash.Add(m.Dy);
                    hash.Add(m.Dz);
                    hash.Add((int)m.Solid);
                    if (m.Face is { } face)
                    {
                        hash.Add(face.Dx);
                        hash.Add(face.Dz);
                    }
                }
            }
        }
        return hash.ToHashCode();
    }
}
