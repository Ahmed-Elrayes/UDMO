using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.Interfaces;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Create
{
    public class CreateCharacterEncyclopediaCommandHandler : IRequestHandler<CreateCharacterEncyclopediaCommand, CharacterEncyclopediaDTO>
    {
        private readonly ICharacterCommandsRepository _repository;

        public CreateCharacterEncyclopediaCommandHandler(ICharacterCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<CharacterEncyclopediaDTO> Handle(CreateCharacterEncyclopediaCommand request, CancellationToken cancellationToken)
        {
            return await _repository.CreateCharacterEncyclopediaAsync(request.CharacterEncyclopedia);
        }
    }
}