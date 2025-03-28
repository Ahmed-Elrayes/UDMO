﻿using DigitalWorldOnline.Commons.Enums.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetEventMapRaidsQuery : IRequest<GetEventMapRaidsQueryDto>
    {
        public long MapId { get; }
        public int Limit { get; }
        public int Offset { get; }
        public string SortColumn { get; }
        public SortDirectionEnum SortDirection { get; }
        public string? Filter { get; }

        public GetEventMapRaidsQuery(
            long mapId,
            int page,
            int pageSize,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter = null)
        {
            MapId = mapId;
            Limit = pageSize;
            Offset = page * pageSize;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            Filter = filter;
        }
    }
}

