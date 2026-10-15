using FluentValidation;
using PickToLight.Application.Dtos;

namespace PickToLight.Application.Dogrulama;

public sealed class SiparisOlusturIstekDogrulayici : AbstractValidator<SiparisOlusturIstek>
{
    public SiparisOlusturIstekDogrulayici()
    {
        RuleFor(x => x.SiparisNo)
            .NotEmpty().WithMessage("Sipariş numarası zorunludur.")
            .MaximumLength(30);

        RuleFor(x => x.Kalemler)
            .NotEmpty().WithMessage("Sipariş en az bir kalem içermelidir.");

        RuleForEach(x => x.Kalemler).ChildRules(k =>
        {
            k.RuleFor(i => i.UrunId).GreaterThan(0);
            k.RuleFor(i => i.Miktar).GreaterThan(0).WithMessage("Miktar sıfırdan büyük olmalı.");
        });
    }
}

public sealed class UrunIstekDogrulayici : AbstractValidator<UrunIstek>
{
    public UrunIstekDogrulayici()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Ad).NotEmpty().MaximumLength(150);
        RuleFor(x => x.GuvenlikStogu).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BirimAgirlikG).GreaterThan(0).When(x => x.BirimAgirlikG.HasValue);
    }
}

public sealed class KutuIstekDogrulayici : AbstractValidator<KutuIstek>
{
    public KutuIstekDogrulayici()
    {
        RuleFor(x => x.Kod).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CihazMac)
            .NotEmpty()
            .Matches("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$")
            .WithMessage("Cihaz MAC adresi AA:BB:CC:DD:EE:FF biçiminde olmalıdır.");
        RuleFor(x => x.BaslangicStogu).GreaterThanOrEqualTo(0);
    }
}

public sealed class GirisIstekDogrulayici : AbstractValidator<GirisIstek>
{
    public GirisIstekDogrulayici()
    {
        RuleFor(x => x.KullaniciAdi).NotEmpty();
        RuleFor(x => x.Parola).NotEmpty().MinimumLength(4);
    }
}

public sealed class SayimIstekDogrulayici : AbstractValidator<SayimIstek>
{
    public SayimIstekDogrulayici() => RuleFor(x => x.YeniAdet).GreaterThanOrEqualTo(0);
}

public sealed class StokIslemIstekDogrulayici : AbstractValidator<StokIslemIstek>
{
    public StokIslemIstekDogrulayici() => RuleFor(x => x.Miktar).GreaterThan(0);
}
