﻿using MediatR;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.Models.Asset;

namespace DigitalWorldOnline.Application.Separar.Queries
{
    public class CashShopAssetsQuery : IRequest<List<CashShopAssetDTO>>
    {

    }
}