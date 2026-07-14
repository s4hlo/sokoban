namespace Sokoban3D.Core;

/// <summary>
/// Confirmação de duas etapas ("aperte de novo pra confirmar"), num lugar só: a primeira
/// <see cref="Press"/> de um token ARMA (devolve false); repetir o MESMO token confirma (devolve
/// true e desarma). Trocar de token re-arma no novo. Qualquer outra ação chama <see cref="Clear"/>.
/// Compartilhado por quem usa o padrão — o descarte de edições não salvas do editor e o gatilho do
/// solver (pra um P sem querer não custar os ~30s de busca).
/// </summary>
public sealed class RepeatConfirm
{
    private object _armed;

    /// <summary>Há uma confirmação armada esperando a segunda batida?</summary>
    public bool Pending => _armed is not null;

    /// <summary>
    /// Registra uma batida do <paramref name="token"/>: true se é a SEGUNDA do mesmo token
    /// (confirmado, e desarma); false se apenas armou (primeira batida, ou troca de token).
    /// </summary>
    public bool Press(object token)
    {
        if (Equals(_armed, token))
        {
            _armed = null;
            return true;
        }
        _armed = token;
        return false;
    }

    /// <summary>Desarma o que estiver pendente — uma ação que invalida o "repita pra confirmar".</summary>
    public void Clear() => _armed = null;
}
