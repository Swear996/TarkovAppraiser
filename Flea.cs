namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Flea market fee (commission) - formula from the EFT wiki ("Trading"):
//    fee = VO * Ti * 4^PO * Q  +  VR * Tr * 4^PR * Q
//    VO = base price of the item, VR = asking price, Ti = Tr = 0.03, Q = 1 (one item)
//    PO = log10(VO / VR), raised to the power 1.08 when VR < VO
//    PR = log10(VR / VO), raised to the power 1.08 when VR >= VO
//  Intelligence Center level 3: 30 % less fee.
//  Cheap base items sold for a lot pay a lot: base 500 sold for 35,000 -> about 44 % fee.
// ------------------------------------------------------------
static class Flea
{
    const double Ti = 0.03, Tr = 0.03;

    public static long Fee(long basePrice, long price, bool intelCenter)
    {
        if (basePrice <= 0 || price <= 0) return 0;
        double vo = basePrice, vr = price;
        double po = Math.Log10(vo / vr);
        double pr = Math.Log10(vr / vo);
        if (vr < vo) po = Math.Pow(po, 1.08);    // po > 0 here
        else pr = Math.Pow(pr, 1.08);            // pr >= 0 here
        double fee = vo * Ti * Math.Pow(4, po) + vr * Tr * Math.Pow(4, pr);
        if (intelCenter) fee *= 0.7;
        return (long)Math.Ceiling(fee);
    }

    // What actually arrives when the item is listed at the current lowest price; 0 = unknown / not possible
    public static long Net(Item i, AppSettings s)
    {
        if (i.NoFlea || i.LastLow <= 0 || i.BasePrice <= 0) return 0;
        return Math.Max(0, i.LastLow - Fee(i.BasePrice, i.LastLow, s.IntelCenter));
    }
}
